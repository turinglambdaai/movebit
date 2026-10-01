#lang racket/base

;; Port of MoveBit.Tests/ConfigStoreTests.cs.

(require rackunit
         racket/file
         json
         "../movebit/config.rkt")

(define (make-temp-dir prefix)
  (define dir (make-temporary-file (string-append "movebit-" prefix "-~a") 'directory))
  dir)

;; Save_and_load_roundtrip_all_settings
(let* ([dir (make-temp-dir "cfg")]
       [expected (reminder-config 50 40 7 #f 4 35 25 #f 25 30 #f #f #t "auto")])
  (check-true (save-config dir expected))
  (define actual (load-config dir))
  (check-equal? (reminder-config-sit-reminder-minutes actual) 50)
  (check-equal? (reminder-config-water-reminder-minutes actual) 40)
  (check-equal? (reminder-config-away-reset-minutes actual) 7)
  (check-false (reminder-config-force-break-enabled actual))
  (check-equal? (reminder-config-break-duration-minutes actual) 4)
  (check-equal? (reminder-config-skip-after-seconds actual) 35)
  (check-equal? (reminder-config-snooze-minutes actual) 25)
  (check-false (reminder-config-micro-break-enabled actual))
  (check-equal? (reminder-config-micro-break-interval-minutes actual) 25)
  (check-equal? (reminder-config-micro-break-duration-seconds actual) 30)
  (check-false (reminder-config-sound-enabled actual))
  (check-false (reminder-config-auto-check-updates actual))
  (check-true (reminder-config-welcome-shown actual))
  ;; atomic write: no .tmp residue
  (check-false (file-exists? (build-path dir "config.json.tmp")))
  (delete-directory/files dir))

;; TrySave_returns_false_when_config_directory_is_a_file
(let* ([dir (make-temp-dir "cfgblk")]
       [blocked-path (build-path dir "not-a-directory")])
  (display-to-file "block directory creation" blocked-path)
  (check-false (save-config blocked-path (default-config)))
  (delete-directory/files dir))

;; Load_clamps_numeric_values_from_disk
(let* ([dir (make-temp-dir "clamp")]
       [path (build-path dir "config.json")])
  (call-with-output-file path
    (lambda (out)
      (write-json
       (hasheq 'SitReminderMinutes 1
               'WaterReminderMinutes 999
               'AwayResetMinutes 0
               'BreakDurationMinutes 100
               'SkipAfterSeconds -10
               'SnoozeMinutes 999
               'MicroBreakIntervalMinutes 1
               'MicroBreakDurationSeconds 999)
       out)))
  (define actual (load-config dir))
  (check-equal? (reminder-config-sit-reminder-minutes actual) 10)
  (check-equal? (reminder-config-water-reminder-minutes actual) 180)
  (check-equal? (reminder-config-away-reset-minutes actual) 1)
  (check-equal? (reminder-config-break-duration-minutes actual) 30)
  (check-equal? (reminder-config-skip-after-seconds actual) 0)
  (check-equal? (reminder-config-snooze-minutes actual) 60)
  (check-equal? (reminder-config-micro-break-interval-minutes actual) 10)
  (check-equal? (reminder-config-micro-break-duration-seconds actual) 60)
  (check-true (reminder-config-auto-check-updates actual))
  (delete-directory/files dir))

;; Corrupt_json_falls_back_to_defaults
(let* ([dir (make-temp-dir "corrupt")]
       [path (build-path dir "config.json")])
  (display-to-file "{ definitely not json" path)
  (define actual (load-config dir))
  (check-equal? (reminder-config-sit-reminder-minutes actual) 45)
  (check-equal? (reminder-config-water-reminder-minutes actual) 30)
  (check-equal? (reminder-config-snooze-minutes actual) 10)
  (check-true (reminder-config-force-break-enabled actual))
  (check-true (reminder-config-auto-check-updates actual))
  (delete-directory/files dir))

;; Config_from_before_snooze_setting_uses_default_delay
(let* ([dir (make-temp-dir "partial")]
       [path (build-path dir "config.json")])
  (display-to-file #<<EOF
{
  "SitReminderMinutes": 60,
  "WelcomeShown": true
}
EOF
                   path)
  (define actual (load-config dir))
  (check-equal? (reminder-config-sit-reminder-minutes actual) 60)
  (check-equal? (reminder-config-snooze-minutes actual) 10)
  (check-true (reminder-config-welcome-shown actual))
  (delete-directory/files dir))

;; Save_clamps_out_of_range_values_too (clamp on save, per the port contract)
(let* ([dir (make-temp-dir "saveclamp")])
  (save-config dir (reminder-config 1 999 0 #t 100 -10 999 #t 1 999 #t #t #f "klingon"))
  (define actual (load-config dir))
  (check-equal? (reminder-config-sit-reminder-minutes actual) 10)
  (check-equal? (reminder-config-water-reminder-minutes actual) 180)
  (check-equal? (reminder-config-snooze-minutes actual) 60)
  (check-equal? (reminder-config-language actual) "auto")
  (delete-directory/files dir))
