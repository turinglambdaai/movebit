#lang racket/base

;; MoveBit Rivet backend entry. Owns the 30-second tick thread, the scheduler
;; + orchestrator runtime, config/history persistence, and the typed RPC
;; surface the native hosts talk to. Hosts are thin shells: they render state
;; and forward user actions; all policy lives here.
;;
;; IMPORTANT (Rivet threading model): `current-event-emitter` lives on the
;; server reader thread. Threads spawned inside RPC handlers inherit it, but a
;; module-top-level thread (started before serve-fds) has NO emitter. The tick
;; thread is therefore started lazily from the `init` RPC — the host calls it
;; right after connecting, and the thread inherits the emitter from the
;; request worker. racket/tests/backend-server-test.rkt proves this at the
;; RVT1 protocol level.
;;
;; All time crosses the RPC boundary as Int64 epoch milliseconds (RVT1 has no
;; float); durations are Int64 milliseconds and stats are Int64 minutes.

(require rivet/backend
         racket/format
         racket/string
         "../racket/movebit/autostart.rkt"
         "../racket/movebit/config.rkt"
         "../racket/movebit/history.rkt"
         "../racket/movebit/idle.rkt"
         "../racket/movebit/orchestrator.rkt"
         "../racket/movebit/paths.rkt"
         "../racket/movebit/scheduler.rkt"
         "../racket/movebit/timeutil.rkt")

(provide start
         ;; test seams: parameters inherited by the tick thread when the host
         ;; (or a test server) parameterizes them around serve/serve-fds
         current-tick-interval-seconds
         current-now-ms
         current-idle-provider
         current-data-dir
         backend-version)

(define backend-version "1.4.0-rivet.1")

;; --- Injectable seams (defaults = the real world) ------------------------

(define current-tick-interval-seconds (make-parameter 30.0))
(define current-now-ms (make-parameter now-ms))
(define current-idle-provider
  (make-parameter
   (lambda ()
     (with-handlers ([exn:fail? (lambda (_) (make-null-idle-provider))])
       (make-platform-idle-provider)))))
(define current-data-dir (make-parameter (movebit-data-dir)))

;; --- Typed surface --------------------------------------------------------

(define-enum ReminderKind (sit water micro))

(define-record ReminderConfig
  ([sit-reminder-minutes : Int64]
   [water-reminder-minutes : Int64]
   [away-reset-minutes : Int64]
   [force-break-enabled : Bool]
   [break-duration-minutes : Int64]
   [skip-after-seconds : Int64]
   [snooze-minutes : Int64]
   [micro-break-enabled : Bool]
   [micro-break-interval-minutes : Int64]
   [micro-break-duration-seconds : Int64]
   [sound-enabled : Bool]
   [auto-check-updates : Bool]
   [welcome-shown : Bool]
   [language : String]))

(define-record DayStats
  ([date : String]
   [active-mins : Int64]
   [sit-breaks : Int64]
   [water-reminders : Int64]
   [micro-breaks : Int64]
   [longest-session-mins : Int64]))

(define-record PauseState
  ([paused : Bool]
   [until-ms : (Optional Int64)]))

(define-record LoopInfo
  ([kind : ReminderKind]
   [due-in-active-mins : (Optional Int64)]
   [accumulated-mins : Int64]))

(define-record BreakState
  ([ends-at-ms : Int64]
   [skip-after-ms : Int64]
   [started-at-ms : Int64]))

(define-record RemindersDue
  ([kinds : (List ReminderKind)]
   [merged : Bool]))

(define-record BreakStarted
  ([duration-ms : Int64]
   [skip-after-ms : Int64]))

(define-record BreakEnded
  ([completed : Bool]))

;; --- Runtime wiring -------------------------------------------------------

(struct runtime (scheduler orchestrator history data-dir idle-provider)
  #:transparent)

;; (box (or/c #f runtime?))
(define runtime-box (box #f))
;; (box (or/c #f thread?)) — the lazy 30 s tick thread, started by `init`.
(define tick-thread-box (box #f))

(define (require-runtime)
  (or (unbox runtime-box)
      (error 'movebit "backend is not initialized; call init first")))

(define minute-ms 60000)

(define (ms->mins ms)
  (quotient (max 0 ms) minute-ms))

(define (config->dto c)
  (ReminderConfig
   (reminder-config-sit-reminder-minutes c)
   (reminder-config-water-reminder-minutes c)
   (reminder-config-away-reset-minutes c)
   (reminder-config-force-break-enabled c)
   (reminder-config-break-duration-minutes c)
   (reminder-config-skip-after-seconds c)
   (reminder-config-snooze-minutes c)
   (reminder-config-micro-break-enabled c)
   (reminder-config-micro-break-interval-minutes c)
   (reminder-config-micro-break-duration-seconds c)
   (reminder-config-sound-enabled c)
   (reminder-config-auto-check-updates c)
   (reminder-config-welcome-shown c)
   (reminder-config-language c)))

(define (dto->config dto)
  (clamp-config
   (reminder-config
    (record-ref dto 'sit-reminder-minutes)
    (record-ref dto 'water-reminder-minutes)
    (record-ref dto 'away-reset-minutes)
    (record-ref dto 'force-break-enabled)
    (record-ref dto 'break-duration-minutes)
    (record-ref dto 'skip-after-seconds)
    (record-ref dto 'snooze-minutes)
    (record-ref dto 'micro-break-enabled)
    (record-ref dto 'micro-break-interval-minutes)
    (record-ref dto 'micro-break-duration-seconds)
    (record-ref dto 'sound-enabled)
    (record-ref dto 'auto-check-updates)
    (record-ref dto 'welcome-shown)
    (record-ref dto 'language))))

(define (stats->dto stats)
  (DayStats (day-stats-date-key stats)
            (ms->mins (day-stats-active-ms stats))
            (day-stats-sit-reminders stats)
            (day-stats-water-reminders stats)
            (day-stats-micro-breaks stats)
            (ms->mins (day-stats-longest-session-ms stats))))

(define (stats->record stats)
  (day-record (ms->mins (day-stats-active-ms stats))
              (day-stats-sit-reminders stats)
              (day-stats-water-reminders stats)
              (day-stats-micro-breaks stats)
              (ms->mins (day-stats-longest-session-ms stats))))

(define (kind->symbol kind) (enum-case kind))
(define (symbol->kind k) (ReminderKind k))

;; --- States (kept in sync with the runtime; hosts sync via $state) --------
;; Defined after the conversion helpers; `init` publishes the real values.

(define-state today : DayStats
  (DayStats (ms->date-key (now-ms)) 0 0 0 0 0))
;; Named active-config: a state called `config` collides with the RPC
;; set-config in the native client generators (both normalize to setConfig).
(define-state active-config : ReminderConfig
  (config->dto (default-config)))
(define-state paused : PauseState
  (PauseState #f (void)))
(define-state loops : (List LoopInfo) '())
(define-state break-active : (Optional BreakState) (void))

;; --- Events ---------------------------------------------------------------

(define-event tick-stats : DayStats)
(define-event reminders-due : RemindersDue)
(define-event break-started : BreakStarted)
(define-event break-ended : BreakEnded)
(define-event day-completed : DayStats)
(define-event config-changed : ReminderConfig)

;; --- State publishers -----------------------------------------------------

(define (loops->dto rt)
  (define scheduler (runtime-scheduler rt))
  (define c (scheduler-config scheduler))
  (define (loop-dto kind accum-ms interval-mins enabled?)
    (LoopInfo (symbol->kind kind)
              (if enabled?
                  (quotient (max 0 (- (* interval-mins minute-ms) accum-ms)) minute-ms)
                  (void))
              (quotient accum-ms minute-ms)))
  (list
   (loop-dto 'sit
             (movebit-scheduler-sit-accum-ms scheduler)
             (reminder-config-sit-reminder-minutes c)
             #t)
   (loop-dto 'water
             (movebit-scheduler-water-accum-ms scheduler)
             (reminder-config-water-reminder-minutes c)
             #t)
   (loop-dto 'micro
             (movebit-scheduler-micro-accum-ms scheduler)
             (reminder-config-micro-break-interval-minutes c)
             (reminder-config-micro-break-enabled c))))

(define (publish-stats! rt)
  (define scheduler (runtime-scheduler rt))
  (define dto (stats->dto (scheduler-stats scheduler)))
  (state-set! today dto)
  (state-set! loops (loops->dto rt))
  (tick-stats dto))

(define (publish-paused! rt)
  (define until (scheduler-paused-until-ms (runtime-scheduler rt)))
  (state-set! paused
              (if until
                  (PauseState #t until)
                  (PauseState #f (void)))))

(define (publish-break! rt)
  (define o (runtime-orchestrator rt))
  (define info (orchestrator-break-info o))
  (state-set! break-active
              (if info
                  (BreakState (break-info-ends-at-ms info)
                              (break-info-skip-after-ms info)
                              (break-info-started-at-ms info))
                  (void))))

;; Periodic flush (~every 5 minutes), exit flush, update-restart flush.
(define (flush-today! rt)
  (define scheduler (runtime-scheduler rt))
  (define stats (scheduler-stats scheduler))
  (history-save-day! (runtime-history rt)
                     (day-stats-date-key stats)
                     (stats->record stats)
                     (day-stats-date-key stats)))

;; --- Init -----------------------------------------------------------------

(define-rpc (initialize : Void)
  (unless (unbox runtime-box)
    (define data-dir (current-data-dir))
    (define loaded-config (load-config data-dir))
    (define idle ((current-idle-provider)))
    (define history (make-history-store data-dir))
    (history-load! history)
    (define scheduler
      (make-scheduler loaded-config
                       (current-now-ms)
                       (idle-provider-get-idle-seconds idle)
                       #:on-day-completed
                       (lambda (closing)
                         ;; Archive the closing day BEFORE the reset.
                         (history-save-day! history
                                            (day-stats-date-key closing)
                                            (stats->record closing)
                                            (day-stats-date-key closing))
                         (day-completed (stats->dto closing)))))
    (define o
      (make-orchestrator scheduler
                         (current-now-ms)
                         #:on-break-started
                         (lambda (duration-ms skip-after-ms)
                           (break-started (BreakStarted duration-ms skip-after-ms)))
                         #:on-break-ended
                         (lambda (completed?) (break-ended (BreakEnded completed?)))
                         #:on-reminders-due
                         (lambda (kinds merged?)
                           (reminders-due
                            (RemindersDue (map symbol->kind kinds) merged?)))
                         #:on-flush
                         (lambda () (flush-today! rt))))
    (define rt (runtime scheduler o history data-dir idle))
    (set-box! runtime-box rt)
    ;; A mid-day restart keeps the morning's activity: seed the persisted
    ;; same-day record into the fresh session before any flush can clobber it.
    (define today-key (day-stats-date-key (scheduler-stats scheduler)))
    (define recent (history-get-recent history 1 today-key))
    (when (and (pair? recent) (string=? (car (car recent)) today-key))
      (orchestrator-restore-today! o (cdr (car recent))))
    ;; Publish initial state and start the lazy tick thread (inherits the
    ;; event emitter + injection parameters from this request thread).
    (state-set! active-config (config->dto loaded-config))
    (publish-stats! rt)
    (publish-paused! rt)
    (publish-break! rt)
    (unless (unbox tick-thread-box)
      (set-box! tick-thread-box
                (thread (lambda () (tick-loop rt))))))
  (void))

;; --- Tick thread ----------------------------------------------------------

(define (tick-loop rt)
  (let loop ()
    (sleep (current-tick-interval-seconds))
    ;; One bad tick (transient FFI failure, host hiccup) must never kill the
    ;; reminder thread — the tray app keeps working.
        (with-handlers ([exn:fail? (lambda (e)
                                 (log-error 'movebit "tick failed: ~a" e))])
      (orchestrator-tick! (runtime-orchestrator rt))
      (publish-stats! rt)
      (publish-paused! rt)
      (publish-break! rt))
    (loop)))

;; --- Reminder actions -----------------------------------------------------

(define-rpc (skip-break : Void)
  (define rt (require-runtime))
  (orchestrator-skip-break! (runtime-orchestrator rt))
  (publish-stats! rt)
  (publish-break! rt)
  (void))

(define-rpc (complete-break : Void)
  ;; Host may request this when its countdown UI finishes; the scheduler stays
  ;; authoritative (a request while no break runs is a harmless no-op).
  (define rt (require-runtime))
  (orchestrator-complete-break! (runtime-orchestrator rt))
  (publish-stats! rt)
  (publish-break! rt)
  (void))

(define-rpc (snooze [kind : ReminderKind] : Void)
  (define rt (require-runtime))
  (orchestrator-snooze! (runtime-orchestrator rt) (kind->symbol kind))
  (state-set! loops (loops->dto rt))
  (void))

(define-rpc (pause-1h : Void)
  (define rt (require-runtime))
  (orchestrator-pause-1h! (runtime-orchestrator rt))
  (publish-paused! rt)
  (void))

(define-rpc (resume : Void)
  (define rt (require-runtime))
  (orchestrator-resume! (runtime-orchestrator rt))
  (publish-paused! rt)
  (void))

;; --- Config ---------------------------------------------------------------

(define-rpc (set-config [value : ReminderConfig] : Void)
  (define rt (require-runtime))
  (define clamped (dto->config value))
  ;; All writes are immediate auto-save; failure keeps in-memory settings.
  (save-config (runtime-data-dir rt) clamped)
  (set-scheduler-config! (runtime-scheduler rt) clamped)
  (state-set! active-config (config->dto clamped))
  (state-set! loops (loops->dto rt))
  (config-changed (config->dto clamped))
  (void))

;; --- History --------------------------------------------------------------

(define-rpc (get-history [days : Int64] : (List DayStats))
  (define rt (require-runtime))
  (define scheduler (runtime-scheduler rt))
  ;; Retention caps meaningful history at 370 days; keep huge requests sane.
  (define recent
    (history-get-recent (runtime-history rt)
                        (min 370 (inexact->exact days))
                        (day-stats-date-key (scheduler-stats scheduler))))
  (for/list ([entry (in-list recent)])
    (define r (cdr entry))
    (DayStats (car entry)
              (day-record-active-minutes r)
              (day-record-sit-breaks r)
              (day-record-water-reminders r)
              (day-record-micro-breaks r)
              (day-record-longest-session-minutes r))))

;; --- Autostart (domain-owned; port of Services/AutoStart.cs) ---------------

(define-rpc (get-autostart : Bool)
  (autostart-enabled?))

(define-rpc (set-autostart [enabled : Bool] : Void)
  (set-autostart! enabled)
  (void))

;; --- Diagnostics & lifecycle ---------------------------------------------

(define-rpc (get-diagnostics : String)
  (define rt (require-runtime))
  (define scheduler (runtime-scheduler rt))
  (define c (scheduler-config scheduler))
  (define stats (scheduler-stats scheduler))
  (string-join
   (list
    (~a "movebit " backend-version)
    (~a "platform: " (system-type) " / " (system-type 'machine))
    (~a "data-dir: " (runtime-data-dir rt))
    (~a "idle-provider: " (idle-provider-name (runtime-idle-provider rt)))
    (~a "today: " (day-stats-date-key stats)
        " active " (ms->mins (day-stats-active-ms stats)) "m"
        " sit " (day-stats-sit-reminders stats)
        " water " (day-stats-water-reminders stats)
        " micro " (day-stats-micro-breaks stats)
        " longest " (ms->mins (day-stats-longest-session-ms stats)) "m")
    (~a "config: sit " (reminder-config-sit-reminder-minutes c)
        "m water " (reminder-config-water-reminder-minutes c)
        "m away " (reminder-config-away-reset-minutes c)
        "m force " (reminder-config-force-break-enabled c)
        " micro " (reminder-config-micro-break-enabled c)
        " language " (reminder-config-language c))
    (~a "paused: " (scheduler-paused-until-ms scheduler))
    (~a "break-active: " (orchestrator-break-active? (runtime-orchestrator rt))))
   "\n"))

;; Hosts call this on exit / before an update restart so at most a few seconds
;; of today's activity are lost (port of FlushToday-on-exit).
(define-rpc (flush-now : Void)
  (orchestrator-flush! (runtime-orchestrator (require-runtime)))
  (void))

;; --- Entry point ----------------------------------------------------------

(define (start in-fd out-fd)
  (serve-fds in-fd out-fd))
