#lang racket/base

;; HKCU Run-key access for login autostart (Windows only). Split out of
;; autostart.rkt and bound lazily via get-ffi-obj so loading this module on
;; macOS/Linux never touches advapi32 (raco test compiles it everywhere).
;; Port of AutoStart.cs: the stored value is REG_SZ with the QUOTED exe path
;; so Program Files style spaces survive command-line parsing.

(require ffi/unsafe)

(provide registry-value-present?
         registry-set-value!
         registry-delete-value!)

(define value-name "MoveBit")
(define run-key "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run")

(define _HKEY _fpointer)
(define HKEY_CURRENT_USER (cast #x80000001 _sint64 _fpointer))
(define key-read  #x20019)  ; KEY_READ
(define key-write #x20006)  ; KEY_SET_VALUE
(define reg-sz 1)

(define advapi32-cache #f)

(define (advapi32)
  (unless advapi32-cache
    (set! advapi32-cache (ffi-lib "Advapi32.dll")))
  advapi32-cache)

(define (reg-open)
  (get-ffi-obj "RegOpenKeyExW" (advapi32)
               (_fun _HKEY _string/utf-16 _uint32 _uint32
                     (out : (_ptr o _HKEY)) -> (rcode : _sint32)
                     -> (and (zero? rcode) out))))
(define (reg-close)
  (get-ffi-obj "RegCloseKey" (advapi32) (_fun _HKEY -> _sint32)))
(define (reg-query)
  (get-ffi-obj "RegQueryValueExW" (advapi32)
               (_fun _HKEY _string/utf-16 _pointer _pointer _pointer _pointer
                     -> _sint32)))
(define (reg-set)
  (get-ffi-obj "RegSetValueExW" (advapi32)
               (_fun _HKEY _string/utf-16 _uint32 _uint32 _bytes _uint32
                     -> _sint32)))
(define (reg-delete)
  (get-ffi-obj "RegDeleteValueW" (advapi32)
               (_fun _HKEY _string/utf-16 -> _sint32)))

(define (call-with-run-key access proc)
  (define key ((reg-open) HKEY_CURRENT_USER run-key 0 access))
  (unless key (error 'autostart "could not open the HKCU Run key"))
  (dynamic-wind
    void
    (lambda () (proc key))
    (lambda () ((reg-close) key))))

;; Whether the MoveBit value exists under the Run key. Unreadable states
;; report #f (disabled), matching the old app's best-effort behavior.
(define (registry-value-present?)
  (call-with-run-key
   key-read
   (lambda (key)
     (define size (malloc 4 'raw))
     (define data (malloc 1024 'raw))
     (dynamic-wind
       void
       (lambda ()
         (ptr-set! size _uint32 1024)
         (zero? ((reg-query) key value-name #f #f data size)))
       (lambda () (free size) (free data))))))

;; exe-path: quoted inside here so callers pass the bare path.
(define (registry-set-value! exe-path)
  (call-with-run-key
   key-write
   (lambda (key)
     (define value
       (string->bytes/utf-8 (string-append "\"" exe-path "\"\0")))
     (define code ((reg-set) key value-name 0 reg-sz
                   value (bytes-length value)))
     (unless (zero? code)
       (error 'autostart "RegSetValueExW failed: ~a" code)))))

(define (registry-delete-value!)
  (call-with-run-key
   key-write
   (lambda (key)
     (define code ((reg-delete) key value-name))
     ;; 2 = ERROR_FILE_NOT_FOUND: already absent is success.
     (unless (or (zero? code) (= code 2))
       (error 'autostart "RegDeleteValueW failed: ~a" code)))))
