#lang racket/base

;; Update-flow tests on the rivet/distribution engine (the threadly
;; test-suite shape, ported): the offline trust chain — craft a manifest,
;; sign it with a throwaway Ed25519 key, verify it, and select updates the
;; same way the live checker does — then the REAL download paths against a
;; local TCP server: the manifest-verified chain into the swap chain
;; (chmod -> rename -> staged apply helper via prepare-install!), the
;; AppImage sibling-asset flow with its sidecar checksum, the persistent
;; 4-hour silent-check throttle stamp, and the failed-check contract
;; against an unreachable feed.
;;
;; (fetch-update-manifest itself only accepts HTTPS, so the live-feed
;; signature compatibility of the embedded key is the release pipeline's
;; job; perform-check! against a real feed is production traffic.)

(require crypto
         crypto/all
         json
         rackunit
         rivet/distribution
         racket/file
         racket/list
         racket/path
         racket/string
         racket/system
         racket/tcp
         (file "../movebit/paths.rkt")
         (prefix-in upd: "../movebit/update.rkt"))

(use-all-factories!)

;; Every persistent touch lands in a throwaway home: the bootstrap (the
;; per-machine stamp store) and the update data dir.
(define test-home (make-temporary-file "movebit-updater-home-~a" 'directory))
(putenv "MOVEBIT_BOOTSTRAP_DIR" (path->string test-home))
(define test-data-dir (make-temporary-file "movebit-updater-data-~a" 'directory))

(define download-limit (* 512 1024 1024))

;; Rivet main grew a trailing `component` discriminator on updater-config and
;; update-artifact; the release pin (v0.11.0) predates it. Fixtures carry the
;; plain component-less artifact (#f) either way (see movebit/update.rkt).
(define component-tail
  (if (equal? (procedure-arity updater-config) 10) (list #f) '()))

;; ---- platform identity ------------------------------------------------------

(test-case "platform symbols match rivet release manifests"
  (case (system-type 'os)
    [(macosx) (check-equal? (upd:platform-symbol) 'macos)]
    [(windows) (check-equal? (upd:platform-symbol) 'windows)]
    [else (check-equal? (upd:platform-symbol) 'linux)])
  (check-not-false (memq (upd:architecture-symbol) '(arm64 x64))))

;; ---- embedded release identity ----------------------------------------------

(test-case "embedded identity is coherent and the key is configured"
  (check-true (upd:movebit-update-configured?))
  (check-equal? upd:update-key-id "movebit-2026-10")
  (check-equal? upd:default-update-base-url
                "https://github.com/turinglambdaai/movebit/releases/latest/download"))

;; ---- AppImage sibling-asset derivation ----------------------------------------

;; The feed's linux entry and the sibling .AppImage release asset share the
;; release-contract arch spelling (x64|arm64), so the derivation is only the
;; extension swap.
(test-case "appimage asset derivation swaps the extension"
  (check-equal? (upd:appimage-asset-name "movebit-0.3.1-linux-x64.tar.gz")
                "movebit-0.3.1-linux-x64.AppImage")
  (check-equal? (upd:appimage-asset-name "movebit-1.0.0-linux-arm64.tar.gz")
                "movebit-1.0.0-linux-arm64.AppImage")
  ;; A non-tar.gz feed entry cannot derive a sibling — refuse loudly.
  (check-exn exn:fail?
             (lambda () (upd:appimage-asset-name "movebit-0.3.1-windows-x64.zip"))))

;; ---- offline trust chain (throwaway keypair, same DER shapes the
;; ---- release pipeline uses) ------------------------------------------------------

(define priv (generate-private-key 'eddsa '((curve ed25519))))
(define pub (datum->pk-key (pk-key->datum priv 'rkt-public) 'rkt-public))

;; The install layout a staged payload must carry: macOS zips movebit.app,
;; Windows zips ARE the install layout (RivetHost.exe at the root).
(define payload-layout
  (case (system-type 'os)
    [(macosx) '("MoveBit.app" "Contents" "MacOS" "MoveBit")]
    [else '("RivetHost.exe")]))
(define payload-root-name (first payload-layout))

(define payload-bytes (make-bytes 300000 42))
(define payload-zip (make-temporary-file "movebit-payload-~a.zip"))
;; `tar` writes the archive; bsdtar (macOS, Windows — the only platforms
;; that ever extract) reads it back by content, so a POSIX tar under a .zip
;; name exercises extract-zip! without a zip writer here. Linux never
;; extracts: prepare-install! refuses there (asserted below).
(let ([staging (make-temporary-file "movebit-payload-src-~a" 'directory)])
  (make-directory* (apply build-path staging (drop-right payload-layout 1)))
  (call-with-output-file (apply build-path staging payload-layout)
    (lambda (out) (write-bytes payload-bytes out)))
  (unless (system* (find-executable-path "tar")
                   "-cf" (path->string payload-zip)
                   "-C" (path->string staging)
                   payload-root-name)
    (error 'update-test "tar could not stage the payload"))
  (delete-directory/files staging))
(define payload-sha256 (sha256-file/hex payload-zip))

;; A manifest whose single artifact points at artifact-url but carries the
;; local payload's signed size + digest.
(define (test-manifest artifact-url)
  (update-manifest
   "site.jrtx.movebit" "9.9.9" 7 'stable
   "2026-10-10T00:00:00Z" "0.0.0" #f #t 100
   (list (apply update-artifact (upd:platform-symbol) (upd:architecture-symbol)
                artifact-url
                payload-sha256
                (file-size payload-zip)
                'zip '()
                component-tail))))

(test-case "signed manifest verifies, a wrong key-id fails closed"
  (define wrapped (open-output-bytes))
  (write-signed-manifest (test-manifest "https://dl.example/movebit-9.9.9.zip")
                         priv "movebit-test" wrapped)
  (define verified
    (verify-signed-manifest (open-input-bytes (get-output-bytes wrapped))
                            pub #:key-id "movebit-test"))
  (check-equal? (update-manifest-version verified) "9.9.9")
  (check-exn exn:fail?
             (lambda ()
               (verify-signed-manifest
                (open-input-bytes (get-output-bytes wrapped))
                pub #:key-id "some-other-key"))))

(test-case "select-update picks this platform's artifact and gates versions"
  (define config
    (apply updater-config "site.jrtx.movebit" "0.0.1" 'stable
           (upd:platform-symbol) (upd:architecture-symbol)
           pub "movebit-test" 0 download-limit
           component-tail))
  (define candidate
    (select-update config
                   (test-manifest "https://dl.example/movebit-9.9.9.zip")))
  (check-true (update-candidate? candidate))
  (check-equal? (update-artifact-size (update-candidate-artifact candidate))
                (file-size payload-zip))
  ;; the same feed at/below the running version selects nothing
  (define current-config
    (struct-copy updater-config config [current-version "9.9.9"]))
  (check-false
   (select-update current-config
                  (test-manifest "https://dl.example/movebit-9.9.9.zip"))))

;; ---- local artifact server ------------------------------------------------------
;; A minimal one-shot HTTP file server: every connection gets the bytes for
;; the path it asked for, so the manifest-verified download and the AppImage
;; sidecar flow can share the same shape.

(define (serve-files! responses)
  ;; responses: (list (cons path-suffix bytes))
  (define listener (tcp-listen 0 16 #f "127.0.0.1"))
  (define-values (_bind-host port-number _peer-host _peer-port)
    (tcp-addresses listener #t))
  (define server
    (thread
     (lambda ()
       (let accept-loop ()
         (define-values (in out) (tcp-accept listener))
         (thread
          (lambda ()
            (define request-line (read-line in 'return-linefeed))
            (define path
              (if (eof-object? request-line)
                  ""
                  (let ([m (regexp-match #px"^\\w+\\s+(\\S+)" request-line)])
                    (if m (second m) ""))))
            (let drain ()
              (define line (read-line in 'return-linefeed))
              (unless (or (eof-object? line) (equal? line "") (equal? line "\r"))
                (drain)))
            (define body
              (for/or ([entry (in-list responses)])
                (and (string-suffix? path (car entry)) (cdr entry))))
            (if body
                (begin
                  (write-string
                   (format "HTTP/1.0 200 OK\r\nContent-Length: ~a\r\n\r\n"
                           (bytes-length body))
                   out)
                  (write-bytes body out))
                (write-string
                 "HTTP/1.0 404 Not Found\r\nContent-Length: 0\r\n\r\n" out))
            (close-output-port out)
            (close-input-port in)))
         (accept-loop)))))
  (values port-number
          (lambda ()
            (kill-thread server)
            (tcp-close listener))))

;; The download runs on a backend-style worker thread; the test polls the
;; same state box the hosts poll through get-update-state until a terminal
;; phase lands.
(define (wait-for-terminal-phase! timeout-seconds)
  (let loop ([deadline (+ (current-seconds) timeout-seconds)])
    (define phase (hash-ref (upd:update-state-snapshot) 'phase))
    (if (or (equal? phase "downloaded")
            (equal? phase "error")
            (> (current-seconds) deadline))
        phase
        (begin (sleep 0.05) (loop deadline)))))

;; ---- manifest-verified download chain -> prepare-install ------------------------

(test-case "download fetches and verifies, prepare-install stages and writes the helper"
  (define payload (file->bytes payload-zip))
  (define-values (port-number stop-server!)
    (serve-files! (list (cons "/movebit-9.9.9.zip" payload))))
  (define config
    (apply updater-config "site.jrtx.movebit" "0.1.0" 'stable
           (upd:platform-symbol) (upd:architecture-symbol)
           pub "movebit-test" 0 download-limit
           component-tail))
  (define candidate
    (select-update config
                   (test-manifest
                    (format "http://127.0.0.1:~a/movebit-9.9.9.zip" port-number))))
  (check-true (update-candidate? candidate))

  (parameterize ([upd:current-install-dir test-data-dir])
    (upd:reset-update-state!)
    (upd:set-candidate! candidate)
    (upd:start-download! test-data-dir)
    ;; a mismatch here is almost always the payload/manifest digest — show
    ;; the state message next to the phase
    (define phase (wait-for-terminal-phase! 120))
    (check-equal? phase "downloaded"
                  (format "~s" (hash-ref (upd:update-state-snapshot) 'message)))
    (check-equal? (hash-ref (upd:update-state-snapshot) 'percent) 100)
    (define downloaded
      (hash-ref (upd:update-state-snapshot) 'downloadedPath #f))
    (check-true (and downloaded (file-exists? downloaded)))

    (case (upd:platform-symbol)
      [(linux)
       ;; AppImage swaps happen at download time; tarball/package installs
       ;; are manual — prepare-install! refuses honestly. platform-symbol,
       ;; not system-type's os ('unix on Linux — the same trap the module
       ;; had).
       (check-exn exn:fail? (lambda () (upd:prepare-install! test-data-dir)))]
      [else
       (define helper (upd:prepare-install! test-data-dir))
       (check-true (file-exists? helper))
       (check-equal? (hash-ref (upd:update-state-snapshot) 'phase)
                     "ready-to-apply")
       ;; the staged layout must be complete (the helper's swap trusts it)
       (define staged (build-path test-data-dir "updates" "staged"))
       (check-true
        (file-exists?
         (if (eq? (system-type 'os) 'macosx)
             (apply build-path staged payload-layout)
             (build-path staged "RivetHost.exe"))))
       ;; and the helper script must carry its swap recipe
       (check-true
        (string-contains? (file->string helper)
                          (if (eq? (system-type 'os) 'macosx)
                              "STAGED_BUNDLE"
                              "robocopy")))]))

  (stop-server!))

;; ---- AppImage flavor (in-place swap: sidecar check, chmod, rename) -----------

(test-case "appimage download verifies its sidecar and swaps in place"
  ;; The mechanics run wherever permissions exist; the production flow is
  ;; Linux-only (AppImage installs), and Windows rejects permission bits
  ;; outright (the threadly suite gates the same way).
  (unless (eq? (system-type 'os) 'windows)
    (define payload (make-bytes 65536 7))
    (define payload-digest (bytes->hex-string (sha256-bytes payload)))
    (define-values (port-number stop-server!)
      (serve-files!
       (list (cons "/movebit-9.9.9-linux-x64.AppImage.sha256"
                   (string->bytes/utf-8
                    (format "~a  movebit.AppImage\n" payload-digest)))
             (cons "/movebit-9.9.9-linux-x64.AppImage" payload))))
    (define image-dir (make-temporary-file "movebit-appimage-~a" 'directory))
    (define image-path (build-path image-dir "movebit.AppImage"))
    (call-with-output-file image-path #:exists 'truncate
      (lambda (out) (write-bytes (make-bytes 16 3) out)))
    ;; $APPIMAGE is the install-shape signal the embedded backend inherits;
    ;; point it at a fake running image so the swap stays in the test's own
    ;; temp dir.
    (putenv "APPIMAGE" (path->string image-path))
    (dynamic-wind
      void
      (lambda ()
        ;; flavor detection follows the platform symbol AND the env marker;
        ;; the swap mechanics below run on any POSIX platform
        (check-equal? (upd:appimage-flavor?)
                      (and (eq? (upd:platform-symbol) 'linux)
                           (let ([v (getenv "APPIMAGE")])
                             (and v (not (string=? v ""))))))
        ;; The feed entry is the linux tar.gz no matter where the mechanics
        ;; run — pin the config AND the artifact to linux (the manifest
        ;; helper pins to the running platform, wrong for this flow).
        (define config
          (apply updater-config "site.jrtx.movebit" "0.1.0" 'stable
                 'linux (upd:architecture-symbol)
                 pub "movebit-test" 0 download-limit
                 component-tail))
        (define candidate
          (select-update config
                         (update-manifest
                          "site.jrtx.movebit" "9.9.9" 7 'stable
                          "2026-10-10T00:00:00Z" "0.0.0" #f #t 100
                          (list (apply update-artifact
                                        'linux (upd:architecture-symbol)
                                        (format "http://127.0.0.1:~a/movebit-9.9.9-linux-x64.tar.gz"
                                                port-number)
                                        payload-digest
                                        (bytes-length payload)
                                        'zip '()
                                        component-tail)))))
        (check-true (update-candidate? candidate))
        (upd:reset-update-state!)
        (upd:set-candidate! candidate)
        (define swapped (upd:download-appimage-with-progress! candidate))
        (check-equal? swapped (path->string image-path))
        (check-equal? (file-size image-path) (bytes-length payload))
        (check-equal? (sha256-file/hex image-path) payload-digest)
        (check-equal? (hash-ref (upd:update-state-snapshot) 'percent) 100)
        ;; the staged temp image was renamed over the running one, and the
        ;; replaced image is executable — the exact regression 2f5632f
        ;; fixed (the vector-form chmod killed every AppImage update)
        (check-equal? (length (directory-list image-dir)) 1)
        (check-not-false
         (memq 'execute (file-or-directory-permissions image-path #f))))
      (lambda ()
        (putenv "APPIMAGE" "")
        (delete-file image-path)
        (delete-directory image-dir)))
    (check-false (upd:appimage-flavor?))  ; env marker restored
    (stop-server!)))

;; ---- persistent silent-check throttle ------------------------------------------

(test-case "the 4-hour throttle stamp is persistent and settled-only"
  (define bootstrap (build-path test-home "bootstrap.json"))
  ;; no stamp yet -> not throttled
  (check-false (upd:update-check-throttled?))
  ;; a settled check (here: the deterministic no-feed-URL refusal) refreshes
  ;; the stamp and lands it in bootstrap.json
  (define result (upd:perform-check! ""))
  (check-equal? (hash-ref result 'status) "error")
  (check-true (upd:update-check-throttled?))
  (define doc (with-input-from-file bootstrap read-json))
  (check-true (exact-integer? (hash-ref doc 'last-update-check)))
  ;; an old stamp does not throttle
  (save-bootstrap (hash-set (load-bootstrap) 'last-update-check 0))
  (check-false (upd:update-check-throttled?)))

(test-case "a transient check failure never refreshes the stamp"
  ;; stamp says "checked recently", then the feed is unreachable
  (upd:record-update-check-stamp!)
  (define before (hash-ref (load-bootstrap) 'last-update-check))
  (define result (upd:perform-check! "https://127.0.0.1:9/movebit"))
  (check-equal? (hash-ref result 'status) "error")
  (check-equal? (hash-ref (upd:update-state-snapshot) 'phase) "error")
  ;; the stamp is untouched — the next launch retries instead of waiting
  (check-equal? (hash-ref (load-bootstrap) 'last-update-check) before))

;; ---- state box contract ----------------------------------------------------------

;; The hosts' poll loop relies on these exact keys (phase/percent/message/
;; availableVersion/downloadedPath) surviving every transition.
(test-case "reset returns the state box to idle"
  (upd:reset-update-state!)
  (define snapshot (upd:update-state-snapshot))
  (check-equal? (hash-ref snapshot 'phase) "idle")
  (check-equal? (hash-ref snapshot 'percent) 0)
  (check-false (hash-ref snapshot 'availableVersion))
  (check-false (hash-ref snapshot 'downloadedPath)))

(delete-file payload-zip)
