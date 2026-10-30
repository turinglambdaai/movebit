#lang racket/base
;; Windows hosted runners may fail reading the OS ROOT certificate store.
;; Use trusted Mozilla CA roots without disabling TLS/hostname verification.
(require openssl
         pkg
         racket/file)

(define bundle (getenv "MOVEBIT_CI_CA_BUNDLE"))
(unless (and bundle
             (file-exists? bundle)
             (regexp-match? #rx#"-----BEGIN CERTIFICATE-----"
                            (file->bytes bundle)))
  (error 'ci-install-windows-deps
         "trusted Mozilla PEM CA bundle is missing or invalid"))

(ssl-default-verify-sources (list bundle))

(define rivet-pin (getenv "RIVET_PIN"))
(unless (and rivet-pin (not (equal? rivet-pin "")))
  (error 'ci-install-windows-deps "RIVET_PIN must be set"))

(pkg-install-command #:auto #t #:no-docs #t
                     (format "https://github.com/turlamai/rivet.git#~a" rivet-pin))
