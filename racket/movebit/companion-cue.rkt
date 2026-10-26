#lang racket/base

;; Companion presentation policy, intentionally isolated from scheduling.
;;
;; Reminder timing, counts, snooze and away-reset remain owned by scheduler.rkt
;; and orchestrator.rkt. This module maps ONE already-coalesced reminders-due
;; event into ONE display cue, without side effects, timers or platform UI.
;;
;; Host capability inputs (quiet/presentation mode, reduced motion) are
;; explicit and may be different on macOS, Windows and Linux. This does NOT
;; detect fullscreen windows or claim pointer pass-through support.
;;
;; Existing users remain on the legacy presentation unless the host opts in.

(require racket/list)

(provide (struct-out companion-cue)
         plan-companion-cue)

;; kinds:      complete, unique list ordered by priority (sit water micro)
;; gesture:    'stretch | 'water | 'blink (the host supplies original artwork)
;; delivery:   'legacy | 'quiet | 'companion
;; animated?:  #f when focus mode or OS Reduce Motion disallows animation
(struct companion-cue (kinds gesture delivery animated?) #:transparent)

(define known-kinds '(sit water micro))

(define (plan-companion-cue kinds
                            #:companion-enabled? [companion-enabled? #f]
                            #:quiet? [quiet? #f]
                            #:reduce-motion? [reduce-motion? #f])
  (unless (and (list? kinds)
               (andmap (lambda (kind) (and (symbol? kind)
                                          (memq kind known-kinds)))
                       kinds))
    (raise-argument-error
     'plan-companion-cue "(listof (or/c 'sit 'water 'micro))" kinds))
  (unless (boolean? companion-enabled?)
    (raise-argument-error 'plan-companion-cue "boolean?" companion-enabled?))
  (unless (boolean? quiet?)
    (raise-argument-error 'plan-companion-cue "boolean?" quiet?))
  (unless (boolean? reduce-motion?)
    (raise-argument-error 'plan-companion-cue "boolean?" reduce-motion?))
  ;; Input order and duplicates must not produce different gestures on
  ;; different hosts. Keep the FULL due-kind set for later/snooze actions.
  (define ordered
    (filter (lambda (kind) (memq kind kinds)) known-kinds))
  (cond
    [(null? ordered) #f]
    [else
     (define gesture
       (cond [(memq 'sit ordered) 'stretch]
             [(memq 'water ordered) 'water]
             [else 'blink]))
     (define delivery
       (cond [(not companion-enabled?) 'legacy]
             [quiet? 'quiet]
             [else 'companion]))
     (companion-cue ordered
                    gesture
                    delivery
                    (and (eq? delivery 'companion)
                         (not reduce-motion?)))]))
