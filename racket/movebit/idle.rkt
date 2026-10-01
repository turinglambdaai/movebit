#lang racket/base

;; Idle detection interface, port of Services/IIdleProvider.cs.
;;
;; A provider is a struct wrapping (-> real-or-#f): idle SECONDS, or #f when
;; the platform/session cannot provide trustworthy idle data. #f is treated as
;; ACTIVE by the scheduler (fail-closed: never invent idle time), which
;; preserves MoveBit's natural-time fallback.
;;
;; Platform FFI providers live in sibling modules and are loaded lazily via
;; dynamic-require so a module only ever loads on its own platform. Tests
;; exercise the null provider and fake providers.

(require racket/runtime-path
         racket/string)

(provide (struct-out idle-provider)
         make-null-idle-provider
         make-constant-idle-provider
         make-macos-idle-provider
         make-windows-idle-provider
         make-linux-wayland-idle-provider
         make-linux-x11-idle-provider
         make-platform-idle-provider)

(struct idle-provider (name get-idle-seconds) #:transparent)

;; Fallback for unsupported platforms/session types: always #f.
(define (make-null-idle-provider)
  (idle-provider 'null (lambda () #f)))

;; Deterministic provider for tests and diagnostics.
(define (make-constant-idle-provider seconds)
  (idle-provider 'constant (lambda () seconds)))

(define-runtime-path macos-module "idle/macos.rkt")
(define-runtime-path windows-module "idle/windows.rkt")
(define-runtime-path x11-module "idle/x11.rkt")
(define-runtime-path wayland-module "idle/wayland.rkt")

(define (require-make module-path)
  ;; Each platform module provides make-idle-provider returning the raw
  ;; (-> real-or-#f) thunk; wrap it in the named provider struct here.
  ((dynamic-require module-path 'make-idle-provider)))

;; Convenience wrappers so callers can build a specific provider explicitly.
(define (make-macos-idle-provider)
  (idle-provider 'macos (require-make macos-module)))
(define (make-windows-idle-provider)
  (idle-provider 'windows (require-make windows-module)))
(define (make-linux-wayland-idle-provider)
  (idle-provider 'wayland (require-make wayland-module)))
(define (make-linux-x11-idle-provider)
  (idle-provider 'x11 (require-make x11-module)))

(define (wayland-session?)
  (define v (getenv "XDG_SESSION_TYPE"))
  (and v (string-ci=? v "wayland")))

;; Platform factory, mirroring IdleProviderFactory.Create(): pick the native
;; provider for the running OS/session. Loading a platform module outside its
;; platform is harmless (FFI resolution is deferred to first use and every
;; failure path degrades to the null behavior).
(define (make-platform-idle-provider)
  (with-handlers ([exn:fail? (lambda (_) (make-null-idle-provider))])
    (case (system-type)
      [(macosx) (make-macos-idle-provider)]
      [(windows) (make-windows-idle-provider)]
      [(unix)
       (if (wayland-session?)
           (make-linux-wayland-idle-provider)
           (make-linux-x11-idle-provider))]
      [else (make-null-idle-provider)])))
