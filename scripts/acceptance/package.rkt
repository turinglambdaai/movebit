#lang racket/base
(require json racket/file racket/path racket/system)
(define platform (case (system-type 'os) [(windows) "windows"] [(macosx) "macos"] [(unix) "linux"] [else (error 'acceptance "unsupported platform")]))
(define raco (or (find-executable-path "raco") (error 'acceptance "raco was not found")))
(unless (system* raco "rivet" "package" "--skip-launch-smoke") (error 'acceptance "package failed"))
(define output (build-path ".rivet" "acceptance" platform))
(make-directory* output)
(call-with-output-file (build-path output "package.json") #:exists 'truncate/replace
  (lambda (out) (write-json (hash 'platform platform 'package-verified #t) out) (newline out)))
