#lang racket/base

;; i18n single-source checker: shared/i18n/{zh,en}.json must cover exactly the
;; same key tree, and the copy pools must keep their canonical sizes
;; (water 8, micro 9, deep-night variants 3 each). The v1 reboot (issue #55)
;; removed the fullscreen-break copy: brk.* and copy.breakHintPool are gone.
;; Run: racket scripts/check-i18n.rkt

(require json
         racket/file
         racket/list
         racket/match
         racket/path
         racket/string)

(define root (path-only (path->complete-path (syntax-source #'here))))
;; this script lives in <repo>/scripts/
(define i18n-dir (build-path root ".." "shared" "i18n"))

(define (load-lang name)
  (call-with-input-file (build-path i18n-dir (format "~a.json" name)) read-json))

(define zh (load-lang "zh"))
(define en (load-lang "en"))

;; Flatten nested hashes to dotted leaf paths.
(define (leaves doc [prefix ""])
  (cond
    [(hash? doc)
     (append* (for/list ([(k v) (in-hash doc)])
                (leaves v (string-append prefix (symbol->string k) "."))))]
    [else (list (string-trim prefix "." #:left? #f))]))

(define zh-keys (sort (leaves zh) string<?))
(define en-keys (sort (leaves en) string<?))

(define failed? #f)

(when (not (equal? zh-keys en-keys))
  (set! failed? #t)
  (eprintf "key sets differ:~n  zh-only: ~a~n  en-only: ~a~n"
           (remove* en-keys zh-keys)
           (remove* zh-keys en-keys)))

;; Contract: the counted UI key set plus one structured `copy` section holding
;; the rotating copy pools verbatim. The v1 reboot re-pinned the count when
;; the fullscreen-break copy came out and the Focus/motion/done keys went in.
(define ui-keys (filter (lambda (k) (not (string-prefix? k "copy."))) zh-keys))
(define copy-keys (filter (lambda (k) (string-prefix? k "copy.")) zh-keys))
(unless (= (length ui-keys) 161)
  (set! failed? #t)
  (eprintf "expected 161 UI keys, got ~a~n" (length ui-keys)))
(unless (and (>= (length copy-keys) 6)
             (hash-has-key? (hash-ref zh 'copy) 'celebrateAt))
  (set! failed? #t)
  (eprintf "copy section incomplete: ~a~n" copy-keys))

(define (pool doc name expected)
  (define p (hash-ref (hash-ref doc 'copy) name #f))
  (cond
    [(not (list? p))
     (set! failed? #t)
     (eprintf "copy.~a missing or not a list~n" name)]
    [(not (= (length p) expected))
     (set! failed? #t)
     (eprintf "copy.~a: expected ~a lines, got ~a~n" name expected (length p))]))

(for ([doc (in-list (list zh en))])
  (pool doc 'waterPool 8)
  (pool doc 'microPool 9)
  (pool doc 'sitToastPool 6)
  (pool doc 'lateNightWaterPool 3)
  (pool doc 'lateNightMicroPool 3)
  (unless (equal? (hash-ref (hash-ref doc 'copy) 'celebrateAt #f) '(3 5 8))
    (set! failed? #t)
    (eprintf "copy.celebrateAt must be [3, 5, 8]~n")))

(when failed? (exit 1))
(printf "i18n OK: 167 contract keys (161 UI + copy), pools verbatim, zh/en parity~n")
