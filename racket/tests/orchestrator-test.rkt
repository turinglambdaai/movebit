#lang racket/base

;; Tests for the app-level orchestration ported from the old App.axaml.cs:
;; the same-tick merge rule and the forced-break lifecycle. These rules had no
;; C# unit tests (they lived in the UI layer); here they are pinned so every
;; host shell gets identical interruption behavior.

(require rackunit
         "harness.rkt"
         "../movebit/config.rkt"
         "../movebit/history.rkt"
         "../movebit/orchestrator.rkt"
         "../movebit/scheduler.rkt")

(define minute-ms 60000)

(struct orch-harness (h o breaks-started breaks-ended reminders-due flushes)
  #:transparent)

;; Build a scheduler harness plus its orchestrator with recording callbacks.
(define (make-oh #:sit [sit 45]
                 #:water [water 30]
                 #:force-break [force-break #t]
                 #:micro [micro #f]
                 #:break-duration [break-duration 5]
                 #:skip-after [skip-after 20]
                 #:snooze [snooze 10])
  (define h (make-harness #:sit sit #:water water #:snooze snooze))
  (h-patch-config! h
                   #:micro-enabled (and micro #t)
                   #:micro-interval (or micro 30))
  (set-scheduler-config!
   (h-scheduler h)
   (struct-copy reminder-config (h-config h)
                [force-break-enabled force-break]
                [break-duration-minutes break-duration]
                [skip-after-seconds skip-after]))
  (define breaks-started (box '()))
  (define breaks-ended (box '()))
  (define reminders-due (box '()))
  (define flushes (box 0))
  (define o
    (make-orchestrator (h-scheduler h)
                       (lambda () (h-now-ms h))
                       #:on-break-started
                       (lambda (duration-ms skip-after-ms)
                         (set-box! breaks-started
                                   (cons (list duration-ms skip-after-ms)
                                         (unbox breaks-started))))
                       #:on-break-ended
                       (lambda (completed?)
                         (set-box! breaks-ended
                                   (cons completed? (unbox breaks-ended))))
                       #:on-reminders-due
                       (lambda (kinds merged?)
                         (set-box! reminders-due
                                   (cons (cons kinds merged?)
                                         (unbox reminders-due))))
                       #:on-flush
                       (lambda () (set-box! flushes (add1 (unbox flushes))))))
  (orch-harness h o breaks-started breaks-ended reminders-due flushes))

(define (oh-step! oh ms)
  ;; Advance the virtual clock and drive the ORCHESTRATOR tick (the backend's
  ;; tick thread path), not the bare scheduler tick.
  (h-advance! (orch-harness-h oh) ms)
  (orchestrator-tick! (orch-harness-o oh)))

(define (oh-step-minutes! oh total [step 5])
  (let loop ([remaining (* total minute-ms)] [step-ms (* step minute-ms)])
    (unless (<= remaining 0)
      (define now (if (< remaining step-ms) remaining step-ms))
      (oh-step! oh now)
      (loop (- remaining now) step-ms))))

(define (oh-reminders oh) (reverse (unbox (orch-harness-reminders-due oh))))
(define (oh-breaks-started oh) (reverse (unbox (orch-harness-breaks-started oh))))
(define (oh-breaks-ended oh) (reverse (unbox (orch-harness-breaks-ended oh))))

;; A sit event while forced breaks are enabled starts a break and swallows
;; every other due reminder this tick ("only ONE interruption per tick").
(let ([oh (make-oh #:sit 10 #:water 10 #:force-break #t)])
  (oh-step-minutes! oh 10)
  (check-equal? (length (oh-breaks-started oh)) 1)
  (check-equal? (oh-reminders oh) '()) ; water due the same tick was swallowed
  (check-true (orchestrator-break-active? (orch-harness-o oh))))

;; break-started carries the configured duration and skip delay.
(let ([oh (make-oh #:sit 10 #:force-break #t #:break-duration 7 #:skip-after 30)])
  (oh-step-minutes! oh 10)
  (check-equal? (oh-breaks-started oh)
                (list (list (* 7 minute-ms) (* 30 1000)))))

;; With forced breaks DISABLED, sit merges with water into ONE merged toast.
(let ([oh (make-oh #:sit 10 #:water 10 #:force-break #f)])
  (oh-step-minutes! oh 10)
  (check-equal? (oh-breaks-started oh) '())
  (check-equal? (oh-reminders oh) (list (cons '(sit water) #t))))

;; Water + micro due together merge into one card, never two interruptions.
(let ([oh (make-oh #:sit 300 #:water 30 #:micro 30 #:force-break #f)])
  (oh-step-minutes! oh 30)
  (check-equal? (oh-reminders oh) (list (cons '(water micro) #t))))

;; Micro-only tick: a dedicated micro event (host shows the micro card).
(let ([oh (make-oh #:sit 300 #:water 300 #:micro 30 #:force-break #t)])
  (oh-step-minutes! oh 30)
  (check-equal? (oh-reminders oh) (list (cons '(micro) #f)))
  (check-equal? (oh-breaks-started oh) '()))

;; Single-kind sit toast (force disabled) is not marked merged.
(let ([oh (make-oh #:sit 10 #:water 300 #:force-break #f)])
  (oh-step-minutes! oh 10)
  (check-equal? (oh-reminders oh) (list (cons '(sit) #f))))

;; While a break runs, ticks discard elapsed time: no accumulation, no
;; reminders. The backend stays authoritative and completes at expiry.
(let ([oh (make-oh #:sit 10 #:water 300 #:force-break #t #:break-duration 5)])
  (oh-step-minutes! oh 10) ; sit fires -> break starts
  (check-true (orchestrator-break-active? (orch-harness-o oh)))
  (define active-at-break (h-active-ms (orch-harness-h oh)))
  (oh-step-minutes! oh 4) ; 4 minutes under the break, auto-complete at 5
  (check-equal? (h-active-ms (orch-harness-h oh)) active-at-break)
  (check-true (orchestrator-break-active? (orch-harness-o oh)))
  (oh-step-minutes! oh 1) ; break expires -> complete
  (check-false (orchestrator-break-active? (orch-harness-o oh)))
  (check-equal? (oh-breaks-ended oh) (list #t))
  ;; CompleteBreak resets all cycles AND the continuous session.
  (check-equal? (h-session-ms (orch-harness-h oh)) 0)
  (check-equal? (h-sit-cycle-ms (orch-harness-h oh)) 0))

;; Host-requested complete_break: same reset, break-ended (#t).
(let ([oh (make-oh #:sit 10 #:water 300 #:force-break #t)])
  (oh-step-minutes! oh 10)
  (orchestrator-complete-break! (orch-harness-o oh))
  (check-false (orchestrator-break-active? (orch-harness-o oh)))
  (check-equal? (oh-breaks-ended oh) (list #t))
  (check-equal? (h-session-ms (orch-harness-h oh)) 0))

;; Skip of a forced break = Snooze(sit): break-ended (#f) and the sit cycle
;; re-fires after snooze minutes of ACTIVE time.
(let ([oh (make-oh #:sit 10 #:water 300 #:force-break #t #:snooze 5)])
  (oh-step-minutes! oh 10) ; sit fires and the break starts
  (define baseline (h-fired-count (orch-harness-h oh)))
  (orchestrator-skip-break! (orch-harness-o oh))
  (check-false (orchestrator-break-active? (orch-harness-o oh)))
  (check-equal? (oh-breaks-ended oh) (list #f))
  ;; sit accum = interval - snooze = 5 -> re-fires after 5 more active minutes
  (oh-step-minutes! oh 4 1)
  (check-equal? (h-fired-count (orch-harness-h oh)) baseline)
  (oh-step-minutes! oh 2 1)
  (check-equal? (h-fired-count (orch-harness-h oh)) (+ baseline 1)))

;; A second break start while one runs finishes (not completes) the first.
(let ([oh (make-oh #:sit 10 #:water 300 #:force-break #t #:snooze 5 #:break-duration 30)])
  (oh-step-minutes! oh 10) ; break 1
  (check-equal? (length (oh-breaks-started oh)) 1)
  ;; skip; the snoozed sit re-fires after 5 more active minutes
  (orchestrator-skip-break! (orch-harness-o oh))
  (oh-step-minutes! oh 4 1)
  (check-equal? (length (oh-breaks-started oh)) 1)
  (oh-step-minutes! oh 2 1)
  (check-equal? (length (oh-breaks-started oh)) 2)
  ;; one skip recorded; break 2 is still running
  (check-equal? (oh-breaks-ended oh) (list #f)))

;; Snoozing a merged toast postpones every merged kind (host snoozes each).
(let ([oh (make-oh #:sit 300 #:water 30 #:micro 30 #:force-break #f #:snooze 4)])
  (oh-step-minutes! oh 30 1) ; water + micro merge on tick 30
  (check-equal? (oh-reminders oh) (list (cons '(water micro) #t)))
  (orchestrator-snooze! (orch-harness-o oh) 'water)
  (orchestrator-snooze! (orch-harness-o oh) 'micro)
  ;; accum = interval - snooze = 26 -> both re-fire after 4 more active minutes
  (oh-step-minutes! oh 3 1)
  (check-equal? (h-fired-kinds (orch-harness-h oh)) '(water micro))
  (oh-step-minutes! oh 4 1)
  (check-equal? (h-fired-kinds (orch-harness-h oh)) '(water micro water micro)))

;; Stats flush fires on every 10th tick (~5 minutes at 30 s ticks).
(let ([oh (make-oh #:sit 300 #:water 300)])
  (oh-step-minutes! oh 20 2) ; 10 ticks of 2 minutes
  (check-equal? (unbox (orch-harness-flushes oh)) 1)
  (oh-step-minutes! oh 20 2) ; another 10 ticks
  (check-equal? (unbox (orch-harness-flushes oh)) 2))

;; Orchestrator-flush! flushes on demand (host exit / update restart).
(let ([oh (make-oh #:sit 300 #:water 300)])
  (orchestrator-flush! (orch-harness-o oh))
  (check-equal? (unbox (orch-harness-flushes oh)) 1))

;; Celebrations at 3/5/8 completed sit breaks per day; host owns the copy.
(check-true (should-celebrate? 3))
(check-false (should-celebrate? 4))
(check-true (should-celebrate? 5))
(check-false (should-celebrate? 7))
(check-true (should-celebrate? 8))
(check-false (should-celebrate? 9))
(check-false (should-celebrate? 0))

;; Pause from the tray action: 1 hour, session reset.
(let ([oh (make-oh #:sit 300 #:water 300)])
  (oh-step-minutes! oh 30)
  (orchestrator-pause-1h! (orch-harness-o oh))
  (check-true (h-paused? (orch-harness-h oh)))
  (check-equal? (h-session-ms (orch-harness-h oh)) 0)
  (orchestrator-resume! (orch-harness-o oh))
  (check-false (h-paused? (orch-harness-h oh))))
