#lang racket/base

;; Linux/X11 idle detection through the XScreenSaver extension, port of
;; Services/LinuxX11IdleProvider.cs. Native Wayland sessions intentionally
;; fall back to unknown idle time until a compositor-neutral idle protocol is
;; available to the app.
;;
;; The FFI bindings resolve lazily on first use and are memoized; every
;; failure path degrades to #f.

(require ffi/unsafe)

(provide make-idle-provider)

(define cached-bindings #f)

;; XScreenSaverInfo on LP64: Window (8) int state (4) int kind (4)
;; unsigned long til_or_since (8) unsigned long idle (8)
;; unsigned long eventMask (8).
(define xss-info-type
  (_list-struct _ulong _sint32 _sint32 _ulong _ulong _ulong))

(define (get-bindings)
  (or cached-bindings
      (begin
        (set! cached-bindings
              (with-handlers ([exn:fail? (lambda (_) 'failed)])
                (define x11 (ffi-lib "libX11.so.6"))
                (define xss (ffi-lib "libXss.so.1"))
                (list (get-ffi-obj "XOpenDisplay" x11 (_fun _pointer -> _pointer))
                      (get-ffi-obj "XDefaultScreen" x11 (_fun _pointer -> _sint32))
                      (get-ffi-obj "XRootWindow" x11 (_fun _pointer _sint32 -> _ulong))
                      (get-ffi-obj "XCloseDisplay" x11 (_fun _pointer -> _sint32))
                      (get-ffi-obj "XFree" x11 (_fun _pointer -> _sint32))
                      (get-ffi-obj "XScreenSaverAllocInfo" xss (_fun -> _pointer))
                      (get-ffi-obj "XScreenSaverQueryInfo" xss
                                   (_fun _pointer _ulong _pointer -> _sint32))))
              )
        cached-bindings)))

(define (make-idle-provider)
  (lambda ()
    (define bindings (get-bindings))
    (and (list? bindings)
         (with-handlers ([exn:fail? (lambda (_) #f)])
           (define x-open-display (list-ref bindings 0))
           (define x-default-screen (list-ref bindings 1))
           (define x-root-window (list-ref bindings 2))
           (define x-close-display (list-ref bindings 3))
           (define x-free (list-ref bindings 4))
           (define x-alloc-info (list-ref bindings 5))
           (define x-query-info (list-ref bindings 6))
           (define display (x-open-display #f))
           (cond
             [(or (not display) (eqv? display 0)) #f]
             [else
              (define info (x-alloc-info))
              (begin0
                (cond
                  [(or (not info) (eqv? info 0)) #f]
                  [else
                   (define root (x-root-window display (x-default-screen display)))
                   (if (zero? (x-query-info display root info))
                       #f
                       (let ([idle (list-ref (ptr-ref info xss-info-type) 4)])
                         (if (>= idle 0) (/ idle 1000.0) #f)))])
                (when (and info (not (eqv? info 0))) (x-free info))
                (x-close-display display))])))))
