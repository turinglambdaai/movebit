#lang racket/base

;; macOS session-wide idle detection through CoreGraphics, port of
;; Services/MacOsIdleProvider.cs. The HID system source (state 1,
;; kCGHIDEventTap) with kCGAnyInputEventType (~0u) observes keyboard/mouse
;; activity independently of which application is focused.
;;
;; The FFI binding resolves lazily on first use and is memoized; every failure
;; path degrades to #f so the scheduler falls back to natural time.

(require ffi/unsafe
         racket/math)

(provide make-idle-provider)

(define hid-system-state 1)                 ; kCGHIDEventTap
(define any-input-event (sub1 (expt 2 32))) ; kCGAnyInputEventType (uint max)

(define coregraphics-path
  "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")

;; #f = not tried yet, 'failed = resolve failed, procedure = ready binding.
(define cached-lookup #f)

(define (get-lookup)
  (or cached-lookup
      (begin
        (set! cached-lookup
              (with-handlers ([exn:fail? (lambda (_) 'failed)])
                (define lib (ffi-lib coregraphics-path))
                (get-ffi-obj "CGEventSourceSecondsSinceLastEventType" lib
                             (_fun _sint32 _uint32 -> _double))))
        cached-lookup)))

(define (make-idle-provider)
  (lambda ()
    (define lookup (get-lookup))
    (and (procedure? lookup)
         (with-handlers ([exn:fail? (lambda (_) #f)])
           (define seconds (lookup hid-system-state any-input-event))
           ;; Mirror the C# guard: non-finite or negative results mean the
           ;; platform refused to answer, not "zero idle".
           (if (and (real? seconds)
                    (not (nan? seconds))
                    (not (infinite? seconds))
                    (>= seconds 0))
               seconds
               #f)))))
