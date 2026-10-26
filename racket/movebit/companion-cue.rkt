#lang racket/base

;; MoveBit v1: choose HOW to present an already-due health reminder.
;;
;; The core product is hydration, standing/movement and eye-rest reminders,
;; not a desktop pet. The scheduler/orchestrator still own all timing, merging,
;; pause, snooze, idle and statistics; these choices do not reset any timer.
;; The native host is responsible for safe surfaces, input pass-through,
;; labels, accessibility and presentation capability reporting.
;;
;; v0.x was an experiment with no real users: there is deliberately no legacy
;; presentation flag or compatibility mode. Character-style cues are the
;; first real product default. On unsupported platforms use a non-blocking
;; native notification rather than an interactive or fullscreen overlay.

(require racket/list)

(provide (struct-out companion-cue)
         plan-companion-cue)

;; kinds:     ALL due kinds (priority order); preserve for snooze/actions
;; action:    semantic suggested health action, independent of appearance
;; gesture:   original visual rendering hint for the character
;; delivery:  'character | 'notification | 'quiet
;; animated?: static when Reduce Motion or presentation silence is selected
(struct companion-cue (kinds action gesture delivery animated?) #:transparent)

(define known-kinds '(sit water micro))

(define (plan-companion-cue kinds
                            #:quiet? [quiet? #f]
                            #:surface-supported? [surface-supported? #t]
                            #:reduce-motion? [reduce-motion? #f])
  (unless (and (list? kinds)
               (andmap (lambda (kind) (and (symbol? kind)
                                          (memq kind known-kinds)))
                       kinds))
    (raise-argument-error
     'plan-companion-cue "(listof (or/c 'sit 'water 'micro))" kinds))
  (unless (boolean? quiet?)
    (raise-argument-error 'plan-companion-cue "boolean?" quiet?))
  (unless (boolean? surface-supported?)
    (raise-argument-error 'plan-companion-cue "boolean?" surface-supported?))
  (unless (boolean? reduce-motion?)
    (raise-argument-error 'plan-companion-cue "boolean?" reduce-motion?))

  ;; Deterministic regardless of host event order; one already-merged event
  ;; results in one cue while all kinds remain available for the user action.
  (define ordered
    (filter (lambda (kind) (memq kind kinds)) known-kinds))
  (cond
    [(null? ordered) #f]
    [else
     (define-values (action gesture)
       (cond [(memq 'sit ordered) (values 'stand-and-stretch 'stretch)]
             [(memq 'water ordered) (values 'drink-water 'water)]
             [else (values 'rest-eyes 'blink)]))
     ;; Quiet merely controls presentation. It must never be used as proof
     ;; that the user completed a break or drank water; queue/expiry semantics
     ;; belong to the reminder engine, not the renderer.
     (define delivery
       (cond [quiet? 'quiet]
             [surface-supported? 'character]
             [else 'notification]))
     (companion-cue ordered
                    action
                    gesture
                    delivery
                    (and (eq? delivery 'character)
                         (not reduce-motion?)))]))
