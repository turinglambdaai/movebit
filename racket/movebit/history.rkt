#lang racket/base

;; Daily activity history, a port of Services/HistoryStore.cs. history.json
;; maps "yyyy-MM-dd" day keys to small counter records. The data set is tiny
;; and human-readable, so JSON beats a database. The live day accumulates in
;; the scheduler and is flushed here periodically; saves prune entries outside
;; the 370-day retention window and swap the file in atomically. Legacy
;; four-field records (written before LongestSessionMinutes existed) read
;; back fine with the insight field defaulting to zero.

(require json
         racket/file
         racket/format
         racket/math
         racket/path
         "timeutil.rkt")

(provide (struct-out day-record)
         default-day-record
         day-record->jsexpr
         jsexpr->day-record
         (struct-out history-store)
         make-history-store
         history-load!
         history-observe-longest-session!
         history-save-day!
         history-get-recent
         history-days
         retention-days
         history-path)

(struct day-record
  (active-minutes sit-breaks water-reminders micro-breaks longest-session-minutes)
  #:transparent)

(struct history-store (path days)  ; days: mutable hash key -> day-record
  #:transparent)

(define (default-day-record)
  (day-record 0 0 0 0 0))

(define retention-days 370)

(define (nonneg n)
  (if (and (exact-integer? n) (>= n 0)) n 0))

(define (day-record->jsexpr r)
  (hasheq 'ActiveMinutes (day-record-active-minutes r)
          'SitBreaks (day-record-sit-breaks r)
          'WaterReminders (day-record-water-reminders r)
          'MicroBreaks (day-record-micro-breaks r)
          'LongestSessionMinutes (day-record-longest-session-minutes r)))

;; Tolerant reader: every field defaults to zero when absent or ill-typed, so
;; legacy records (and half-written ones) still load.
(define (jsexpr->day-record js)
  (if (hash? js)
      (day-record
       (nonneg (hash-ref js 'ActiveMinutes 0))
       (nonneg (hash-ref js 'SitBreaks 0))
       (nonneg (hash-ref js 'WaterReminders 0))
       (nonneg (hash-ref js 'MicroBreaks 0))
       (nonneg (hash-ref js 'LongestSessionMinutes 0)))
      (default-day-record)))

(define (history-path directory)
  (build-path directory "history.json"))

(define (make-history-store directory)
  (history-store (history-path directory) (make-hash)))

;; (Re)load the store from disk. Unreadable history is not worth crashing a
;; tray app over: an empty store keeps the session working. racket/json reads
;; object keys as symbols; day keys convert back to their "yyyy-MM-dd" strings.
(define (history-load! store)
  (define days (history-store-days store))
  (hash-clear! days)
  (with-handlers ([exn:fail? (lambda (_) (void))])
    (define js (call-with-input-file (history-store-path store) read-json))
    (when (hash? js)
      (for ([(key value) (in-hash js)])
        (define key-str
          (cond [(string? key) key]
                [(symbol? key) (symbol->string key)]
                [else #f]))
        (when key-str
          (hash-set! days key-str (jsexpr->day-record value))))))
  store)

;; Keep the live session peak without touching the other counters. The
;; periodic SaveDay flush persists it, so insights add no second write timer.
(define (history-observe-longest-session! store date-key minutes)
  (when (and (exact-integer? minutes) (> minutes 0))
    (define existing (hash-ref (history-store-days store) date-key #f))
    (define record (or existing (default-day-record)))
    (when (> minutes (day-record-longest-session-minutes record))
      (hash-set! (history-store-days store)
                 date-key
                 (struct-copy day-record record
                              [longest-session-minutes minutes])))))

;; Upsert one day (keeping the larger stored session peak), prune expired
;; entries, and persist atomically (write-then-swap). Best-effort: returns #f
;; when the write fails while the in-memory store stays usable.
(define (history-save-day! store date-key record [today-key (ms->date-key (now-ms))])
  (define days (history-store-days store))
  (define existing (hash-ref days date-key #f))
  (define merged
    (if (and existing
             (> (day-record-longest-session-minutes existing)
                (day-record-longest-session-minutes record)))
        (struct-copy day-record record
                     [longest-session-minutes
                      (day-record-longest-session-minutes existing)])
        record))
  (hash-set! days date-key merged)
  (prune-expired! store date-key today-key)
  (persist! store))

(define (persist! store)
  (with-handlers ([exn:fail? (lambda (_) #f)])
    (define path (history-store-path store))
    ;; Mirror the C# write path: <file>.tmp in the same directory.
    (define tmp (string->path (string-append (path->string path) ".tmp")))
    (make-directory* (path-only path))
    (call-with-output-file tmp
      (lambda (out)
        (define ordered
          (sort (hash->list (history-store-days store)) string<? #:key car))
        ;; write-json takes symbol object keys; "2026-01-01" round-trips.
        (write-json (for/hasheq ([entry (in-list ordered)])
                      (values (string->symbol (car entry))
                              (day-record->jsexpr (cdr entry))))
                    out)
        (newline out))
      #:exists 'replace)
    (rename-file-or-directory tmp path #t)
    #t))

;; Days kept in the file. Pruning happens on every save. The anchor is the
;; later of the saved day and today, so clock-back situations cannot prune
;; recent data.
(define (prune-expired! store saved-key today-key)
  (define saved (date-key->civil-day saved-key))
  (define today (date-key->civil-day today-key))
  (define anchor
    (cond [(and saved today) (max saved today)]
          [saved saved]
          [today today]
          [else #f]))
  (when anchor
    (define cutoff (- anchor (- retention-days 1)))
    (for ([(key _) (in-hash (history-store-days store))])
      (define parsed (date-key->civil-day key))
      (when (and parsed (< parsed cutoff))
        (hash-remove! (history-store-days store) key)))))

;; The most recent `count` days, oldest first, holes included as zero days.
;; count <= 0 yields an empty list.
(define (history-get-recent store count today-key)
  (if (<= count 0)
      '()
      (for/list ([i (in-range (- count 1) -1 -1)])
        (define key (date-key+days today-key (- i)))
        (cons key
              (hash-ref (history-store-days store) key (default-day-record))))))

;; Snapshot of the in-memory day table (key -> day-record), for diagnostics.
(define (history-days store)
  (hash-copy (history-store-days store)))
