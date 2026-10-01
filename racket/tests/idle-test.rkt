#lang racket/base

;; Port of MoveBit.Tests/LinuxWaylandIdleProviderTests.cs (D-Bus output
;; parsing) plus provider-interface behavior tests. Platform FFI providers are
;; only exercised for null-degradation on the running platform; the fake
;; provider covers the scheduler contract (fail-closed idle).

(require rackunit
         "../movebit/idle.rkt"
         "../movebit/idle/wayland.rkt")

;; Parses_numeric_dbus_payloads
(check-equal? (parse-first-unsigned "t 12345") 12345)
(check-equal? (parse-first-unsigned "(uint64 987654,)") 987654)
(check-equal? (parse-first-unsigned "u 42") 42)
(check-equal? (parse-first-unsigned "(uint32 17,)") 17)

;; Rejects_outputs_without_a_number
(check-false (parse-first-unsigned ""))
(check-false (parse-first-unsigned "error"))
(check-false (parse-first-unsigned "uint64 nope"))

;; Fail-closed: the null provider never invents idle time.
(check-equal? (idle-provider-name (make-null-idle-provider)) 'null)
(check-false ((idle-provider-get-idle-seconds (make-null-idle-provider))))
(check-equal? ((idle-provider-get-idle-seconds (make-constant-idle-provider 125)))
              125)

;; The platform factory returns a working provider structure on any OS and its
;; getter returns seconds or #f without raising (the macOS CoreGraphics FFI is
;; verified separately on real hardware).
(define p (make-platform-idle-provider))
(check-true (idle-provider? p))
(check-true (let ([v ((idle-provider-get-idle-seconds p))])
              (or (not v) (real? v))))
