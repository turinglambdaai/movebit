#lang racket/base

;; CI probe for the Windows autostart Run-key path. The public set-autostart!
;; is best-effort (errors swallowed, old-app parity), so this drives
;; windows-set! directly and lets failures fail LOUDLY. Windows runners only.

(require "../racket/movebit/autostart.rkt")

(define probe-plain "C:\\probe-movebit.exe")
(define probe-spaces "C:\\Program Files\\MoveBit Host.exe")

(define (roundtrip exe)
  (parameterize ([autostart-executable-path exe])
    (with-handlers ([exn:fail? (lambda (e)
                                 (printf "set FAILED: ~a\n" (exn-message e))
                                 #f)])
      (windows-set! #t)
      (define present (windows-value-present?))
      (windows-set! #f)
      (define gone (not (windows-value-present?)))
      (printf "set -> ~s, delete -> gone: ~s (path: ~a)\n" present gone exe)
      (and present gone))))

(define ok-plain (roundtrip probe-plain))
(define ok-spaces (roundtrip probe-spaces))

(unless (and ok-plain ok-spaces)
  (exit 1))
(printf "registry probe OK\n")
