#lang racket/base

;; Date helpers for MoveBit. Day keys are local-time "yyyy-MM-dd" strings, the
;; same storage format the old .NET app used in history.json. Day arithmetic
;; goes through proleptic-Gregorian civil day numbers (Howard Hinnant's
;; algorithms) instead of adding 86400000 ms, so day math stays correct across
;; DST transitions. Clocks are integer milliseconds since the Unix epoch.

(require racket/date
         racket/format
         racket/math)

(provide ms->date-key
         date-key->civil-day
         civil-day->date-key
         date-key+days
         days-between-keys
         local-ymd->ms
         ms->local-date
         now-ms)

;; Floor division/modulo helpers: Racket's `quotient` truncates toward zero,
;; which would corrupt the era math for pre-1970 dates.
(define (floor-div a b) (quotient (- a (modulo a b)) b))

;; days_from_civil: proleptic Gregorian civil day number, 1970-01-01 = 0.
(define (days-from-civil y m d)
  (define yy (if (<= m 2) (- y 1) y))
  (define era (floor-div yy 400))
  (define yoe (- yy (* era 400)))
  (define mp (+ m (if (> m 2) -3 9)))
  (define doy (+ (quotient (+ (* 153 mp) 2) 5) (- d 1)))
  (define doe (+ (* yoe 365) (floor-div yoe 4) (- (floor-div yoe 100)) doy))
  (+ (* era 146097) doe -719468))

;; civil_from_days: inverse of days-from-civil; returns (values y m d).
(define (civil-from-days z)
  (define zz (+ z 719468))
  (define era (floor-div zz 146097))
  (define doe (- zz (* era 146097)))
  (define yoe (floor-div (- doe (floor-div doe 1460) (floor-div doe 146096)) 365))
  (define y (+ yoe (* era 400)))
  (define doy (- doe (+ (* 365 yoe) (floor-div yoe 4) (- (floor-div yoe 100)))))
  (define mp (floor-div (+ (* 5 doy) 2) 153))
  (define d (+ (- doy (floor-div (+ (* 153 mp) 2) 5)) 1))
  (define m (+ mp (if (< mp 10) 3 -9)))
  (values (+ y (if (<= m 2) 1 0)) m d))

(define (ms->local-date ms)
  (seconds->date (floor (/ ms 1000))))

(define (ms->date-key ms)
  (define d (ms->local-date ms))
  (~a (date-year d) "-" (pad2 (date-month d)) "-" (pad2 (date-day d))))

(define (pad2 n) (~r n #:min-width 2 #:pad-string "0"))

;; "yyyy-MM-dd" -> civil day number. Returns #f for unparseable keys.
(define (date-key->civil-day key)
  (define m (regexp-match #px"^([0-9]{4})-([0-9]{2})-([0-9]{2})$" key))
  (and m
       (let ([y (string->number (list-ref m 1))]
             [mo (string->number (list-ref m 2))]
             [d (string->number (list-ref m 3))])
         (and y mo d (>= mo 1) (<= mo 12) (>= d 1) (<= d 31)
              (days-from-civil y mo d)))))

(define (civil-day->date-key day)
  (define-values (y m d) (civil-from-days day))
  (~a y "-" (pad2 m) "-" (pad2 d)))

(define (date-key+days key n)
  (define day (date-key->civil-day key))
  (and day (civil-day->date-key (+ day n))))

;; Whole days from a to b (positive when b is later).
(define (days-between-keys a b)
  (define da (date-key->civil-day a))
  (define db (date-key->civil-day b))
  (and da db (- db da)))

;; UTC epoch milliseconds for a local wall-clock Y/M/D (test harness helper,
;; mirrors the old C# tests' TimeZoneInfo.ConvertTimeToUtc of an Unspecified
;; DateTime). Raises when the local time does not exist (DST gap).
(define (local-ymd->ms y m d [hh 0] [mm 0] [ss 0])
  (* 1000 (find-seconds ss mm hh d m y)))

;; Canonical wall-clock source for the live backend (integer ms).
(define (now-ms)
  (inexact->exact (floor (current-inexact-milliseconds))))
