#lang racket/base

;; Reminder settings, a faithful port of Models/ReminderConfig.cs +
;; Services/ConfigStore.cs. The on-disk format stays compatible with the old
;; .NET app (PascalCase keys in config.json under the movebit data directory),
;; so users can drop the Racket backend onto an existing install. Numeric
;; settings are clamped on load AND save; a corrupt or unreadable file silently
;; falls back to defaults — a tray app must never crash on config.

(require json
         racket/bool
         racket/file
         racket/math)

(provide (struct-out reminder-config)
         default-config
         clamp-config
         config->jsexpr
         jsexpr->config
         load-config
         save-config
         config-path)

(struct reminder-config
  (sit-reminder-minutes      ; 45 (10-240, UI step 5)
   water-reminder-minutes    ; 30 (5-180, UI step 5)
   away-reset-minutes        ; 5  (1-60)
   force-break-enabled       ; #t
   break-duration-minutes    ; 5  (1-30)
   skip-after-seconds        ; 20 (0-120, UI step 5)
   snooze-minutes            ; 10 (5-60, UI step 5)
   micro-break-enabled       ; #t
   micro-break-interval-minutes ; 30 (10-60)
   micro-break-duration-seconds ; 20 (10-60)
   sound-enabled             ; #t
   auto-check-updates        ; #t
   welcome-shown             ; #f
   language)                 ; "auto" | "zh" | "en"
  #:transparent)

(define (default-config)
  (reminder-config 45 30 5 #t 5 20 10 #t 30 20 #t #t #f "auto"))

(define (clamp v lo hi) (min (max v lo) hi))

(define (valid-language? v) (and (string? v) (member v '("auto" "zh" "en"))))

(define (bool-field c accessor)
  (define v (accessor c))
  (if (boolean? v) v (not (false? v))))

;; Clamp every numeric field and normalize language. Applied on load, on save,
;; and on every RPC write — a persisted file can never drift out of range.
(define (clamp-config c)
  (reminder-config
   (clamp (exact-floor (reminder-config-sit-reminder-minutes c)) 10 240)
   (clamp (exact-floor (reminder-config-water-reminder-minutes c)) 5 180)
   (clamp (exact-floor (reminder-config-away-reset-minutes c)) 1 60)
   (bool-field c reminder-config-force-break-enabled)
   (clamp (exact-floor (reminder-config-break-duration-minutes c)) 1 30)
   (clamp (exact-floor (reminder-config-skip-after-seconds c)) 0 120)
   (clamp (exact-floor (reminder-config-snooze-minutes c)) 5 60)
   (bool-field c reminder-config-micro-break-enabled)
   (clamp (exact-floor (reminder-config-micro-break-interval-minutes c)) 10 60)
   (clamp (exact-floor (reminder-config-micro-break-duration-seconds c)) 10 60)
   (bool-field c reminder-config-sound-enabled)
   (bool-field c reminder-config-auto-check-updates)
   (bool-field c reminder-config-welcome-shown)
   (let ([lang (reminder-config-language c)])
     (if (valid-language? lang) lang "auto"))))

(define (config->jsexpr c)
  (define clamped (clamp-config c))
  (hasheq 'SitReminderMinutes (reminder-config-sit-reminder-minutes clamped)
          'WaterReminderMinutes (reminder-config-water-reminder-minutes clamped)
          'AwayResetMinutes (reminder-config-away-reset-minutes clamped)
          'ForceBreakEnabled (reminder-config-force-break-enabled clamped)
          'BreakDurationMinutes (reminder-config-break-duration-minutes clamped)
          'SkipAfterSeconds (reminder-config-skip-after-seconds clamped)
          'SnoozeMinutes (reminder-config-snooze-minutes clamped)
          'MicroBreakEnabled (reminder-config-micro-break-enabled clamped)
          'MicroBreakIntervalMinutes (reminder-config-micro-break-interval-minutes clamped)
          'MicroBreakDurationSeconds (reminder-config-micro-break-duration-seconds clamped)
          'SoundEnabled (reminder-config-sound-enabled clamped)
          'AutoCheckUpdates (reminder-config-auto-check-updates clamped)
          'WelcomeShown (reminder-config-welcome-shown clamped)
          'Language (reminder-config-language clamped)))

;; Tolerant reader: missing keys keep defaults, wrong-typed values keep
;; defaults, numerics are clamped. Matches how System.Text.Json deserialized
;; into C# property defaults for absent members.
(define (jsexpr->config js)
  (cond
    [(hash? js)
     (define (int-field key default lo hi)
       (define v (hash-ref js key default))
       (if (exact-integer? v) (clamp v lo hi) default))
     (define (bool-field key default)
       (define v (hash-ref js key default))
       (if (boolean? v) v default))
     (reminder-config
      (int-field 'SitReminderMinutes 45 10 240)
      (int-field 'WaterReminderMinutes 30 5 180)
      (int-field 'AwayResetMinutes 5 1 60)
      (bool-field 'ForceBreakEnabled #t)
      (int-field 'BreakDurationMinutes 5 1 30)
      (int-field 'SkipAfterSeconds 20 0 120)
      (int-field 'SnoozeMinutes 10 5 60)
      (bool-field 'MicroBreakEnabled #t)
      (int-field 'MicroBreakIntervalMinutes 30 10 60)
      (int-field 'MicroBreakDurationSeconds 20 10 60)
      (bool-field 'SoundEnabled #t)
      (bool-field 'AutoCheckUpdates #t)
      (bool-field 'WelcomeShown #f)
      (let ([lang (hash-ref js 'Language "auto")])
        (if (valid-language? lang) lang "auto")))]
    [else (default-config)]))

(define (config-path directory)
  (build-path directory "config.json"))

;; Load config.json from the given directory. Any read/parse problem falls
;; back to defaults (the old app caught IOException/JsonException/
;; UnauthorizedAccessException for exactly this reason).
(define (load-config directory)
  (define path (config-path directory))
  (with-handlers ([exn:fail? (lambda (_) (default-config))])
    (define js
      (call-with-input-file path read-json))
    (jsexpr->config js)))

;; Atomic write: config.json.tmp -> rename. Returns #t when the file reached
;; disk, #f otherwise (reminders keep working from in-memory settings).
(define (save-config directory config)
  (with-handlers ([exn:fail? (lambda (_) #f)])
    (define path (config-path directory))
    (define tmp (build-path directory "config.json.tmp"))
    (make-directory* directory)
    (call-with-output-file tmp
      (lambda (out)
        (write-json (config->jsexpr (clamp-config config)) out)
        (newline out))
      #:exists 'replace)
    (rename-file-or-directory tmp path #t)
    #t))
