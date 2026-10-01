#lang racket/base

;; App-level orchestration around the scheduler: the same-tick merge rule and
;; the forced-break lifecycle, ported from the old Avalonia App.axaml.cs
;; (OnTimerTick / FlushTickReminders / StartForcedBreak / FinishBreakInternal).
;; The orchestrator is pure domain logic — no UI, no rivet — so the same rules
;; run identically on every host shell.
;;
;; One interruption per tick, ever: a sit event that starts a forced break
;; swallows the rest (the host closes its toasts/micro window), and otherwise
;; every kind due in this tick merges into ONE reminders-due payload; a
;; micro-only tick produces a dedicated micro event. Snoozing a merged toast
;; postpones all merged kinds.

(require "config.rkt"
         "history.rkt"
         "scheduler.rkt")

(provide (struct-out break-info)
         (struct-out movebit-orchestrator)
         make-orchestrator
         orchestrator-tick!
         orchestrator-skip-break!
         orchestrator-complete-break!
         orchestrator-snooze!
         orchestrator-pause-1h!
         orchestrator-resume!
         orchestrator-restore-today!
         orchestrator-flush!
         orchestrator-break-active?
         orchestrator-break-info
         should-celebrate?)

(struct break-info (ends-at-ms skip-after-ms started-at-ms) #:transparent)

(struct movebit-orchestrator
  (scheduler
   now                    ; (-> integer-ms), same clock as the scheduler
   break                  ; #f or break-info
   break-count-today      ; sit-break counter the starting event carried
   pending                ; reversed list of (kind count active-ms) this tick
   flush-counter          ; stats flush roughly every 10 ticks (~5 minutes)
   on-break-started       ; #f or (duration-ms skip-after-ms) -> any
   on-break-ended         ; #f or (completed?) -> any
   on-reminders-due       ; #f or ((kind ...) merged?) -> any
   on-stats-changed       ; #f or (-> any) after each stateful tick
   on-flush)              ; #f or (-> any): persist today's stats now
  #:transparent
  #:mutable)

(define ticks-per-flush 10)          ; ~5 minutes at the 30 s tick
(define hour-ms (* 60 60 1000))

(define (make-orchestrator scheduler now
                            #:on-break-started [on-break-started #f]
                            #:on-break-ended [on-break-ended #f]
                            #:on-reminders-due [on-reminders-due #f]
                            #:on-stats-changed [on-stats-changed #f]
                            #:on-flush [on-flush #f])
  ;; Wire the scheduler's reminder events into the per-tick batch once, the
  ;; same way the old app subscribed ReminderFired at startup. Any previously
  ;; installed callback (tests, diagnostics) stays in the chain.
  (define previous-on-reminder (movebit-scheduler-on-reminder scheduler))
  (define o
    (movebit-orchestrator scheduler now #f 0 '() 0
                          on-break-started on-break-ended on-reminders-due
                          on-stats-changed on-flush))
  (set-movebit-scheduler-on-reminder!
   scheduler
   (lambda (kind count active-ms)
     (when previous-on-reminder (previous-on-reminder kind count active-ms))
     (collect-reminder! o kind count active-ms)))
  o)

(define (orch-config o)
  (scheduler-config (movebit-orchestrator-scheduler o)))

(define (orchestrator-break-active? o)
  (and (movebit-orchestrator-break o) #t))

;; #f when no forced break is running, else break-info.
(define (orchestrator-break-info o)
  (movebit-orchestrator-break o))

;; Collect scheduler reminder callbacks into the per-tick batch.
(define (collect-reminder! o kind count active-ms)
  (set-movebit-orchestrator-pending!
   o (cons (list kind count active-ms) (movebit-orchestrator-pending o))))

(define (take-pending! o)
  (define events (reverse (movebit-orchestrator-pending o)))
  (set-movebit-orchestrator-pending! o '())
  events)

;; First occurrence of each kind, in sit/water/micro priority order.
(define (dedupe-kinds events)
  (for/fold ([acc '()])
            ([e (in-list events)])
    (define kind (car e))
    (if (memq kind acc) acc (append acc (list kind)))))

(define (first-of-kind events kind)
  (findf (lambda (e) (eq? (car e) kind)) events))

;; One interruption per tick, ever. Port of FlushTickReminders.
(define (flush-tick-reminders! o)
  (define events (take-pending! o))
  (when (pair? events)
    (define kinds (dedupe-kinds events))
    (define sit-due (memq 'sit kinds))
    (define water-due (memq 'water kinds))
    (define micro-due (memq 'micro kinds))
    (cond
      ;; A forced break takes over: it is its own notification, and it swallows
      ;; every other due reminder this tick.
      [(and sit-due (reminder-config-force-break-enabled (orch-config o)))
       (start-forced-break! o (first-of-kind events 'sit))]
      ;; Micro-only tick: the host shows a micro card instead of a toast.
      [(and (not sit-due) (not water-due) micro-due)
       (fire-reminders-due! o '(micro) #f)]
      ;; Otherwise merge everything due into one toast; snoozing it postpones
      ;; all merged kinds.
      [else
       (fire-reminders-due! o kinds (> (length kinds) 1))])))

(define (fire-reminders-due! o kinds merged?)
  (define cb (movebit-orchestrator-on-reminders-due o))
  (when cb (cb kinds merged?)))

;; Port of StartForcedBreak (minus the screen enumeration, which stays
;; host-side; if the host cannot enumerate screens it degrades to a toast).
(define (start-forced-break! o sit-event)
  ;; A full-screen break has priority over transient nudges/toasts that may
  ;; already be visible from a previous cycle: finish (not complete) any break.
  (when (orchestrator-break-active? o)
    (finish-break! o #f))
  (define config (orch-config o))
  (define now ((movebit-orchestrator-now o)))
  (define duration-ms (* 60000 (reminder-config-break-duration-minutes config)))
  (define skip-after-ms (* 1000 (reminder-config-skip-after-seconds config)))
  (set-movebit-orchestrator-break!
   o (break-info (+ now duration-ms) skip-after-ms now))
  (set-movebit-orchestrator-break-count-today! o (list-ref sit-event 1))
  (define cb (movebit-orchestrator-on-break-started o))
  (when cb (cb duration-ms skip-after-ms)))

;; Port of OnBreakSkipped: skipping a forced break equals Snooze(sit).
(define (orchestrator-skip-break! o)
  (when (orchestrator-break-active? o)
    (finish-break! o #f)
    (scheduler-snooze! (movebit-orchestrator-scheduler o) 'sit)))

;; Countdown finished (host requested) — a completed break is a real break:
;; restart every reminder cycle, like returning after being away.
(define (orchestrator-complete-break! o)
  (when (orchestrator-break-active? o)
    (finish-break! o #t)))

;; Port of FinishBreakInternal. While a break is active the scheduler never
;; ticks (rest is not work); skipping still discards elapsed wall time.
(define (finish-break! o completed?)
  (set-movebit-orchestrator-break! o #f)
  (set-movebit-orchestrator-break-count-today! o 0)
  (define scheduler (movebit-orchestrator-scheduler o))
  (if completed?
      (scheduler-complete-break! scheduler)
      (scheduler-discard-elapsed! scheduler))
  (define cb (movebit-orchestrator-on-break-ended o))
  (when cb (cb completed?)))

;; Advance one tick: under an active break the scheduler's wall-clock anchor
;; stays current without advancing any cycle or daily active time. Break
;; expiry is detected here so the backend stays authoritative even when the
;; host countdown window is gone. Port of OnTimerTick (minus UI refresh).
(define (orchestrator-tick! o)
  (define break (movebit-orchestrator-break o))
  (define now ((movebit-orchestrator-now o)))
  (cond
    [break
     (if (>= now (break-info-ends-at-ms break))
         (orchestrator-complete-break! o)
         (scheduler-discard-elapsed! (movebit-orchestrator-scheduler o)))]
    [else
     (scheduler-tick! (movebit-orchestrator-scheduler o))])
  (flush-tick-reminders! o)

  ;; Flush today's stats every ~5 minutes so a crash or power loss never
  ;; costs more than a few minutes of history.
  (set-movebit-orchestrator-flush-counter!
   o (modulo (add1 (movebit-orchestrator-flush-counter o)) ticks-per-flush))
  (when (zero? (movebit-orchestrator-flush-counter o))
    (orchestrator-flush! o))

  (define cb (movebit-orchestrator-on-stats-changed o))
  (when cb (cb)))

;; Snooze from a toast: a merged toast stands in for every timer due this
;; tick, so the host snoozes each merged kind. Skipping a forced break is
;; Snooze(sit) (see orchestrator-skip-break!).
(define (orchestrator-snooze! o kind)
  (scheduler-snooze! (movebit-orchestrator-scheduler o) kind))

(define (orchestrator-pause-1h! o)
  (scheduler-pause-for! (movebit-orchestrator-scheduler o) hour-ms))

(define (orchestrator-resume! o)
  (scheduler-resume! (movebit-orchestrator-scheduler o)))

;; Mid-day restart: seed today's persisted record into the fresh scheduler.
;; (The `_restoredFor` idempotence lives in the scheduler.)
(define (orchestrator-restore-today! o record)
  (scheduler-restore-today! (movebit-orchestrator-scheduler o) record))

;; Persist today's stats now (periodic flush, host exit, update restart).
(define (orchestrator-flush! o)
  (define cb (movebit-orchestrator-on-flush o))
  (when cb (cb)))

;; Milestone celebrations fire at 3/5/8 completed sit breaks per day; the
;; host owns the copy, the orchestrator owns the decision.
(define celebrate-at '(3 5 8))

(define (should-celebrate? completed-today)
  (and (integer? completed-today) (memq completed-today celebrate-at) #t))
