#lang racket/base

;; Windows idle detection via GetLastInputInfo, port of
;; Services/Win32IdleProvider.cs. Session-wide (any app's input counts), which
;; is exactly what a sedentary-work monitor needs.
;;
;; The FFI binding resolves lazily on first use and is memoized; every failure
;; path degrades to #f.

(require ffi/unsafe)

(provide make-idle-provider)

(define user32-path "user32")
(define kernel32-path "kernel32")

;; #f = not tried yet, 'failed = resolve failed, values = ready bindings.
(define cached-bindings #f)

(define (get-bindings)
  (or cached-bindings
      (begin
        (set! cached-bindings
              (with-handlers ([exn:fail? (lambda (_) 'failed)])
                (define user32 (ffi-lib user32-path))
                (define kernel32 (ffi-lib kernel32-path))
                (list (get-ffi-obj "GetLastInputInfo" user32
                                   (_fun _pointer -> _sint32))
                      (get-ffi-obj "GetTickCount" kernel32
                                   (_fun -> _uint32))))
              )
        cached-bindings)))

;; LASTINPUTINFO = { uint cbSize; uint dwTime; } — 8 bytes, written manually
;; into a raw block so no generated cstruct machinery is needed.
(define lastinputinfo-size 8)
(define uint32-modulus (expt 2 32))

(define (make-idle-provider)
  (lambda ()
    (define bindings (get-bindings))
    (and (list? bindings)
         (with-handlers ([exn:fail? (lambda (_) #f)])
           (define block (malloc lastinputinfo-size 'raw))
           (ptr-set! block _uint32 0 lastinputinfo-size)
           (define get-last-input-info (list-ref bindings 0))
           (define get-tick-count (list-ref bindings 1))
           (if (= 1 (get-last-input-info block))
               (let ()
                 ;; Both operands are GetTickCount-style uint tick counts; the
                 ;; modulo subtraction handles the ~49.7-day wraparound exactly
                 ;; like the C# unchecked cast.
                 (define last (ptr-ref block _uint32 1))
                 (define now (get-tick-count))
                 (/ (modulo (- now last) uint32-modulus) 1000.0))
               #f)))))
