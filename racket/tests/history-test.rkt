#lang racket/base

;; Port of MoveBit.Tests/HistoryStoreTests.cs plus the legacy-format cases
;; from WorkPatternTests.cs (all history-side behavior lives here).

(require rackunit
         racket/file
         racket/list
         racket/string
         json
         "../movebit/history.rkt"
         "../movebit/timeutil.rkt")

(define (make-temp-dir prefix)
  (make-temporary-file (string-append "movebit-" prefix "-~a") 'directory))

(define today-key (ms->date-key (now-ms)))

;; SaveDay_roundtrips_through_disk
(let* ([dir (make-temp-dir "hist")]
       [yesterday (date-key+days today-key -1)]
       [store (make-history-store dir)])
  (history-load! store)
  (history-save-day! store yesterday (day-record 123 2 4 6 0))
  (define reloaded (history-load! (make-history-store dir)))
  (define entry (findf (lambda (e) (string=? (car e) yesterday))
                       (history-get-recent reloaded 3 today-key)))
  (check-true (pair? entry))
  (check-equal? (day-record-active-minutes (cdr entry)) 123)
  (check-equal? (day-record-sit-breaks (cdr entry)) 2)
  (check-equal? (day-record-water-reminders (cdr entry)) 4)
  (check-equal? (day-record-micro-breaks (cdr entry)) 6)
  (delete-directory/files dir))

;; GetRecent_fills_holes_with_zero_days_oldest_first
(let* ([dir (make-temp-dir "holes")]
       [store (make-history-store dir)])
  (history-load! store)
  (history-save-day! store today-key (day-record 90 1 2 3 0))
  (define week (history-get-recent store 7 today-key))
  (check-equal? (length week) 7)
  (check-equal? (car (car week)) (date-key+days today-key -6))
  (check-equal? (car (list-ref week 6)) today-key)
  (check-equal? (day-record-active-minutes (cdr (list-ref week 6))) 90)
  (check-equal? (day-record-active-minutes (cdr (car week))) 0)
  (for ([day (in-list (take week 6))])
    (check-equal? (day-record-sit-breaks (cdr day)) 0))
  (delete-directory/files dir))

;; SaveDay_overwrites_same_day
(let* ([dir (make-temp-dir "over")]
       [store (make-history-store dir)])
  (history-load! store)
  (history-save-day! store today-key (day-record 10 0 0 0 0))
  (history-save-day! store today-key (day-record 42 1 1 1 0))
  (define single (history-get-recent store 1 today-key))
  (check-equal? (length single) 1)
  (check-equal? (car (car single)) today-key)
  (check-equal? (day-record-active-minutes (cdr (car single))) 42)
  (delete-directory/files dir))

;; SaveDay_prunes_entries_outside_retention_window
(let* ([dir (make-temp-dir "prune")]
       [store (make-history-store dir)]
       [expired (date-key+days today-key (- retention-days))]
       [oldest-kept (date-key+days today-key (- retention-days 1))]
       [path (build-path dir "history.json")])
  (history-load! store)
  (history-save-day! store expired (day-record 10 1 1 1 0) today-key)
  (history-save-day! store oldest-kept (day-record 20 2 2 2 0) today-key)
  (history-save-day! store today-key (day-record 30 3 3 3 0) today-key)
  (define json (file->string path))
  (check-false (string-contains? json expired))
  (check-true (string-contains? json oldest-kept))
  (check-true (string-contains? json today-key))
  (delete-directory/files dir))

;; Old_history_json_without_insight_field_still_loads (from WorkPatternTests)
(let* ([dir (make-temp-dir "legacy")]
       [path (build-path dir "history.json")]
       [store (make-history-store dir)])
  (call-with-output-file path
    (lambda (out)
      (write-json
       (hasheq (string->symbol today-key)
               (hasheq 'ActiveMinutes 120 'SitBreaks 2 'WaterReminders 3 'MicroBreaks 4))
       out)))
  (history-load! store)
  (define entry (car (history-get-recent store 1 today-key)))
  (check-equal? (day-record-active-minutes (cdr entry)) 120)
  (check-equal? (day-record-longest-session-minutes (cdr entry)) 0)
  (delete-directory/files dir))

;; Live_longest_session_survives_existing_four_field_flush (from WorkPatternTests)
(let* ([dir (make-temp-dir "merge")]
       [store (make-history-store dir)])
  (history-load! store)
  (history-observe-longest-session! store today-key 47)
  (history-save-day! store today-key (day-record 90 1 2 3 0))
  (define reloaded (history-load! (make-history-store dir)))
  (define entry (car (history-get-recent reloaded 1 today-key)))
  (check-equal? (day-record-longest-session-minutes (cdr entry)) 47)
  (delete-directory/files dir))

;; ObserveLongestSession_ignores_zero_and_negative_peaks
(let* ([dir (make-temp-dir "obs")]
       [store (make-history-store dir)])
  (history-load! store)
  (history-observe-longest-session! store today-key 0)
  (history-observe-longest-session! store today-key -5)
  (define entry (car (history-get-recent store 1 today-key)))
  (check-equal? (day-record-longest-session-minutes (cdr entry)) 0))

;; Corrupt_history_starts_empty
(let* ([dir (make-temp-dir "badhist")]
       [store (make-history-store dir)])
  (make-directory* dir)
  (display-to-file "not json at all" (build-path dir "history.json"))
  (history-load! store)
  (check-equal? (history-get-recent store 3 today-key)
                (list (cons (date-key+days today-key -2) (default-day-record))
                      (cons (date-key+days today-key -1) (default-day-record))
                      (cons today-key (default-day-record))))
  (delete-directory/files dir))
