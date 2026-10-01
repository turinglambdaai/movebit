#lang racket/base

;; Pure countdown math for the forced-break clock and overlays, ported from
;; Services/BreakCountdown.cs. Display seconds and ring progress derive from
;; the same remaining value so text and visuals never drift apart. All values
;; are milliseconds at module boundaries; the progress fraction is a float in
;; [0,1] for direct ring rendering.

(require racket/math)

(provide remaining-ms
         display-seconds
         progress-fraction)

;; Remaining break time: never negative, never above the total.
(define (remaining-ms total-ms elapsed-ms)
  (cond [(<= total-ms 0) 0]
        [else
         (define r (- total-ms elapsed-ms))
         (cond [(<= r 0) 0]
               [(> r total-ms) total-ms]
               [else r])]))

;; Whole seconds shown on the overlay; rounds up until the break has really
;; finished (1 ms still displays "1", 0 displays "0").
(define (display-seconds remaining)
  (if (<= remaining 0)
      0
      (exact-ceiling (/ remaining 1000))))

;; Ring progress on the same scale as the text, clamped to [0, 1].
(define (progress-fraction remaining total)
  (cond [(<= total 0) 0.0]
        [(<= remaining 0) 0.0]
        [else (min 1.0 (max 0.0 (exact->inexact (/ remaining total))))]))
