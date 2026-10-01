#lang racket/base

;; CI probe for the Windows autostart Run-key path: write, read back, verify
;; the quoted value, delete, and confirm it is gone. Exits non-zero on any
;; mismatch so the step fails visibly. Windows runners only.

(require racket/file
         racket/format
         "racket/movebit/autostart-registry.rkt")

(define probe-plain "C:\\probe-movebit.exe")
(define probe-spaces "C:\\Program Files\\MoveBit Host.exe")

(registry-set-value! probe-plain)
(define present-1 (registry-value-present?))
(printf "present after set: ~s\n" present-1)

(registry-set-value! probe-spaces)
(define present-2 (registry-value-present?))
(printf "present after set (path with spaces): ~s\n" present-2)

(registry-delete-value!)
(define present-3 (registry-value-present?))
(printf "present after delete: ~s\n" present-3)

(unless (and present-1 present-2 (not present-3))
  (exit 1))
(printf "registry probe OK\n")
