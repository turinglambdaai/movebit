#lang racket/base

;; MoveBit v1 first product contract: one useful HEALTH reminder, not a pet
;; event. Pure tests exercise policy without a host/window or real clock.

(require rackunit
         "../movebit/companion-cue.rkt")

;; New product default is a character-delivered, actionable water reminder.
;; There is no opt-in compatibility path from the experimental v0.x.
(let ([cue (plan-companion-cue '(water))])
  (check-equal? (companion-cue-kinds cue) '(water))
  (check-equal? (companion-cue-action cue) 'drink-water)
  (check-equal? (companion-cue-gesture cue) 'water)
  (check-equal? (companion-cue-delivery cue) 'character)
  (check-true (companion-cue-animated? cue)))

;; Every visual hint must have a clear real-world health action.
(let ([cue (plan-companion-cue '(sit))])
  (check-equal? (companion-cue-action cue) 'stand-and-stretch)
  (check-equal? (companion-cue-gesture cue) 'stretch))
(let ([cue (plan-companion-cue '(micro))])
  (check-equal? (companion-cue-action cue) 'rest-eyes)
  (check-equal? (companion-cue-gesture cue) 'blink))

;; A merged scheduler event produces exactly one cue, deterministic priority,
;; and preserves EVERY due kind for downstream Done/Later behavior.
(let ([cue (plan-companion-cue '(micro water sit water))])
  (check-equal? (companion-cue-kinds cue) '(sit water micro))
  (check-equal? (companion-cue-action cue) 'stand-and-stretch)
  (check-equal? (companion-cue-delivery cue) 'character))
(let ([cue (plan-companion-cue '(micro water))])
  (check-equal? (companion-cue-kinds cue) '(water micro))
  (check-equal? (companion-cue-action cue) 'drink-water))

;; Presentation/Focus mode suppresses UI, NOT real reminder state.
(let ([cue (plan-companion-cue '(sit water) #:quiet? #t)])
  (check-equal? (companion-cue-kinds cue) '(sit water))
  (check-equal? (companion-cue-action cue) 'stand-and-stretch)
  (check-equal? (companion-cue-delivery cue) 'quiet)
  (check-false (companion-cue-animated? cue)))

;; An incapable compositor must get a non-blocking native notification
;; rather than a bogus interactive overlay or full-screen interruption.
(let ([cue (plan-companion-cue '(water)
                               #:surface-supported? #f)])
  (check-equal? (companion-cue-delivery cue) 'notification)
  (check-equal? (companion-cue-action cue) 'drink-water)
  (check-false (companion-cue-animated? cue)))

;; Reduce Motion still delivers the health suggestion as a static visual.
(let ([cue (plan-companion-cue '(micro) #:reduce-motion? #t)])
  (check-equal? (companion-cue-delivery cue) 'character)
  (check-equal? (companion-cue-action cue) 'rest-eyes)
  (check-false (companion-cue-animated? cue)))

(check-false (plan-companion-cue '()))
(check-exn exn:fail:contract?
           (lambda () (plan-companion-cue '(unknown))))
(check-exn exn:fail:contract?
           (lambda () (plan-companion-cue "water")))
(check-exn exn:fail:contract?
           (lambda () (plan-companion-cue '(water) #:quiet? 'maybe)))
(check-exn exn:fail:contract?
           (lambda () (plan-companion-cue '(water) #:surface-supported? 'maybe)))
(check-exn exn:fail:contract?
           (lambda () (plan-companion-cue '(water) #:reduce-motion? 'maybe)))

;; The expiry window is a real policy: long enough to survive a slide change,
;; short enough that the character never becomes furniture.
(check-true (> cue-expiry-ms 0))
(check-equal? cue-expiry-ms (* 10 60 1000))

;; Quiet exit collapses queued cues into ONE priority-ordered summary —
;; the bounded "not a pile-up" contract from issue #55.
(check-false (summarize-open-cues '()))
(check-equal? (summarize-open-cues
               (list (open-cue 1 '(water) 'pending 0)
                     (open-cue 2 '(micro) 'pending 5)))
              '(water micro))
;; displayed entries were SEEN before quiet — never re-emitted as a summary
(check-equal? (summarize-open-cues
               (list (open-cue 1 '(water) 'displayed 0)
                     (open-cue 2 '(micro) 'pending 5)))
              '(micro))
;; duplicates collapse: two water moments mean one water kind
(check-equal? (summarize-open-cues
               (list (open-cue 1 '(water) 'pending 0)
                     (open-cue 2 '(water sit) 'pending 5)))
              '(sit water))
;; expired entries have already returned to the normal cycle: not summarized
(check-equal? (summarize-open-cues
               (list (open-cue 1 '(water) 'expired 0)
                     (open-cue 2 '(micro) 'pending 5)))
              '(micro))
;; settled cues (acknowledged/snoozed never reach this list, but contract it)
(check-false (summarize-open-cues
              (list (open-cue 1 '(water) 'expired 0))))
(check-exn exn:fail:contract?
           (lambda () (summarize-open-cues "nope")))
