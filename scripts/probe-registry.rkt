#lang racket/base

;; CI probe for the Windows autostart Run-key path, driven through the public
;; autostart API with a parameterized exe path: set, read back, set a path
;; with spaces, read back, delete, confirm gone. Exits non-zero on any
;; mismatch so the step fails visibly. Windows runners only.

(require racket/format
         "racket/movebit/autostart.rkt")

(define probe-plain "C:\\probe-movebit.exe")
(define probe-spaces "C:\\Program Files\\MoveBit Host.exe")

(define (roundtrip exe)
  (parameterize ([autostart-executable-path exe])
    (set-autostart! #t)
    (define present (autostart-enabled?))
    (set-autostart! #f)
    (define gone (not (autostart-enabled?)))
    (printf "set -> ~s, delete -> gone: ~s (path: ~a)\n" present gone exe)
    (and present gone)))

(define ok-plain (roundtrip probe-plain))
(define ok-spaces (roundtrip probe-spaces))

(unless (and ok-plain ok-spaces)
  (exit 1))
(printf "registry probe OK\n")
