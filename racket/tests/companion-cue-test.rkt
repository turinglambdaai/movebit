#lang racket/base

;; Companion cue policy contracts. No native UI or real-time clock required.

(require rackunit
         "../movebit/companion-cue.rkt")

;; Existing users stay with current gentle reminders until they opt in.
(let ([cue (plan-companion-cue '(water))])
  (check-equal? (companion-cue-delivery cue) 'legacy)
  (check-false (companion-cue-animated? cue))
  (check-equal? (companion-cue-kinds cue) '(water)))

;; A water reminder is expressed as the water gesture.
(let ([cue (plan-companion-cue '(water)
                               #:companion-enabled? #t)])
  (check-equal? (companion-cue-gesture cue) 'water)
  (check-equal? (companion-cue-delivery cue) 'companion)
  (check-true (companion-cue-animated? cue)))

;; One merged scheduler event must generate just one cue. Preserve all
;; included kinds in a deterministic order for later/snooze handling.
(let ([cue (plan-companion-cue '(micro water sit water)
                               #:companion-enabled? #t)])
  (check-equal? (companion-cue-kinds cue) '(sit water micro))
  (check-equal? (companion-cue-gesture cue) 'stretch)
  (check-equal? (companion-cue-delivery cue) 'companion))

;; Water outranks micro, which becomes a subtle blink by itself.
(check-equal? (companion-cue-gesture
               (plan-companion-cue '(micro water)
                                   #:companion-enabled? #t))
              'water)
(check-equal? (companion-cue-gesture
               (plan-companion-cue '(micro)
                                   #:companion-enabled? #t))
              'blink)

;; Quiet/presentation mode means no pet attention request, but the due-kind
;; set is retained: suppression is PRESENTATION policy, not a scheduler reset.
(let ([cue (plan-companion-cue '(sit water)
                               #:companion-enabled? #t
                               #:quiet? #t)])
  (check-equal? (companion-cue-kinds cue) '(sit water))
  (check-equal? (companion-cue-delivery cue) 'quiet)
  (check-false (companion-cue-animated? cue)))

;; Honor reduced motion without erasing the static cue.
(let ([cue (plan-companion-cue '(sit)
                               #:companion-enabled? #t
                               #:reduce-motion? #t)])
  (check-equal? (companion-cue-delivery cue) 'companion)
  (check-equal? (companion-cue-gesture cue) 'stretch)
  (check-false (companion-cue-animated? cue)))

(check-false (plan-companion-cue '()
                                 #:companion-enabled? #t))

;; Reject unrecognized events/flags instead of producing a misleading cue.
(check-exn exn:fail:contract?
           (lambda () (plan-companion-cue '(unknown))))
(check-exn exn:fail:contract?
           (lambda () (plan-companion-cue "water")))
(check-exn exn:fail:contract?
           (lambda () (plan-companion-cue '(water)
                                           #:quiet? 'maybe)))
