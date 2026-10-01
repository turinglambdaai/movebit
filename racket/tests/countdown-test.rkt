#lang racket/base

;; Port of MoveBit.Tests/BreakCountdownTests.cs.

(require rackunit
         racket/math
         "../movebit/countdown.rkt")

(define (approx a b [tolerance 1e-10])
  (<= (abs (- a b)) tolerance))

;; DisplaySeconds_rounds_up_until_break_has_really_finished
(check-equal? (display-seconds 59010) 60)   ; 59.01 s
(check-equal? (display-seconds 1) 1)
(check-equal? (display-seconds 0) 0)

;; ProgressFraction_uses_the_same_remaining_time_as_the_text
(check-true (approx (progress-fraction 150000 300000) 0.5))
(check-equal? (display-seconds 150000) 150)

;; Remaining_clamps_elapsed_time_to_the_break_bounds
(let ([total (* 5 60 1000)])
  (check-equal? (remaining-ms total -1000) total)
  (check-equal? (remaining-ms total (* 1 60 1000)) (* 4 60 1000))
  (check-equal? (remaining-ms total (* 6 60 1000)) 0)
  (check-equal? (remaining-ms 0 1000) 0))

;; ProgressFraction_clamps_to_zero_and_one
(let ([total (* 5 60 1000)])
  (check-equal? (progress-fraction (* 6 60 1000) total) 1.0)
  (check-equal? (progress-fraction 0 total) 0.0)
  (check-equal? (progress-fraction (* 1 60 1000) 0) 0.0))
