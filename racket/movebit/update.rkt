#lang racket/base

;; MoveBit update wiring over `rivet/distribution`, following the fulcrum
;; reference implementation.
;;
;; Trust model (inherited from Rivet): HTTPS protects transport but is not
;; the root of update trust. The channel manifest is Ed25519-signed and
;; verified against the public key embedded below before any version
;; decision. Key management lives in scripts/update-keys.sh: key generation,
;; the exact replacement of `current-update-public-key-hex`, and rotation via
;; the key id (ship a client trusting the next key before signing with it).
;;
;; The backend verifies and downloads the signed update artifact; the host
;; owns installation. A check fetches and verifies the signed channel
;; manifest; the artifact download runs on a background thread with progress
;; published to a state box that the UI polls through the `get-update-state`
;; RPC (RVT1 events are thread-local, so a background thread cannot emit
;; them directly). The feed serves portable archives per platform — on
;; Windows the zip IS the install layout, so `prepare-install!` extracts the
;; verified archive into a staging directory and writes a helper script that
;; waits for the app to exit, swaps the payload, and relaunches.

(require json
         net/base64
         net/url
         rivet/app-info
         rivet/distribution
         racket/file
         racket/format
         racket/path
         racket/port
         racket/string
         racket/system
         ffi/unsafe
         (only-in ffi/unsafe/define define-ffi-definer))

(provide current-update-version
         current-install-dir
         default-update-base-url
         update-key-id
         maximum-download-bytes
         hex->bytes
         movebit-update-configured?
         platform-symbol
         architecture-symbol
         update-state-snapshot
         reset-update-state!
         perform-check!
         start-download!
         prepare-install!)

;; Ed25519 public key, DER SPKI hex-encoded — committed on purpose: the
;; public half only enables verification. Rotation replaces this hex and the
;; key id below (ship clients trusting the next key before signing with it).
;; raw 32 bytes (base64) t4Z+rtAk2NtcgFBUX+r139X1c2EnVOaI/HxkN4vs914= wrapped
;; in the standard SPKI header.
(define current-update-public-key-hex
  (string-append
   "302a300506032b6570032100"
   "b7867eaed024d8db5c8050545feaf5dfd5f573612754e688fc7c64378becf75e"))
(define update-key-id "movebit-2026-10")

(define default-update-base-url
  "https://github.com/turinglambdaai/movebit/releases/latest/download")

(define maximum-download-bytes (* 512 1024 1024))

(define current-update-version (make-parameter "0.0.0"))
;; The application install directory (where RivetHost.exe lives). The host
;; hands it over; headless tests parameterize a temp directory.
(define current-install-dir (make-parameter (current-directory)))

;; ---------- public key ----------

(define (hex->bytes s)
  (let ([n (string-length s)])
    (if (and (even? n) (>= n 2))
        (apply bytes (for/list ([i (in-range 0 n 2)])
                       (string->number (substring s i (+ i 2)) 16)))
        #f)))

(define (movebit-update-configured?)
  (and current-update-public-key-hex
       (hex->bytes current-update-public-key-hex)
       #t))

;; The manifest verifier takes a parsed key object, not raw DER. Lazy on
;; purpose: FFI at module-import time kills embedded apps at startup (the
;; landmine rivet/distribution/crypto.rkt documents); a failure here
;; surfaces as an honest "update check failed" instead of a dead app.
(define (current-update-public-key)
  (bytes->ed25519-public-key (hex->bytes current-update-public-key-hex)))

;; ---------- platform identity ----------

(define (platform-symbol)
  (case (system-type 'os)
    [(macosx) 'macos]
    [(windows) 'windows]
    [else 'linux]))

(define (architecture-symbol)
  (case (system-type 'arch)
    [(aarch64 arm64) 'arm64]
    [else 'x64]))

;; The running version: the packaged app reads rivet-app-info.rktd. The
;; parameter exists for headless tests and for unpackaged dev runs where no
;; app-info file exists (the packaged version still comes from the file).
(define (effective-version)
  (define configured (current-update-version))
  (if (and (not (string=? configured "0.0.0")) configured)
      configured
      (with-handlers ([exn:fail? (lambda (_) configured)])
        (app-version))))

(define (updater-config* maximum-bytes)
  (updater-config "site.jrtx.movebit"
                  (effective-version)
                  'stable
                  (platform-symbol)
                  (architecture-symbol)
                  (current-update-public-key)
                  update-key-id
                  ;; Rollout 0 always matches a manifest rolling out to 100;
                  ;; MoveBit ships to everyone today (bucket 0..99 < 100).
                  0
                  maximum-bytes))

;; ---------- shared update state (UI-visible) ----------

;; phase: idle | checking | downloading | downloaded | ready-to-apply | error
(define update-state
  (box (hasheq 'phase "idle"
               'percent 0
               'message #f
               'availableVersion #f
               'stagedPath #f)))

(define candidate-box (box #f))
(define worker-thread-box (box #f))

(define (state-set! key value)
  (set-box! update-state (hash-set (unbox update-state) key value)))

(define (update-state-snapshot)
  (unbox update-state))

(define (reset-update-state!)
  (set-box! candidate-box #f)
  (set-box! update-state
            (hasheq 'phase "idle"
                    'percent 0
                    'message #f
                    'availableVersion #f
                    'stagedPath #f)))

(define (set-update-error! message)
  (state-set! 'phase "error")
  (state-set! 'message message))

;; ---------- check ----------

(define (perform-check! base-url)
  (reset-update-state!)
  (state-set! 'phase "checking")
  (with-handlers
      ([exn:fail?
        (lambda (e)
          (state-set! 'phase "error")
          (state-set! 'message (exn-message e))
          (hasheq 'status "error" 'message (exn-message e)))])
    (cond
      [(not (movebit-update-configured?))
       (state-set! 'phase "idle")
       (hasheq 'status "error"
               'message "updates unavailable: developer build (no update key configured)")]
      [(or (not base-url) (string=? (string-trim base-url) ""))
       (state-set! 'phase "idle")
       (hasheq 'status "error"
               'message "updates unavailable: no update base URL configured")]
      [else
       (define manifest
         (fetch-update-manifest
          (string-append (regexp-replace* #rx"/+$" base-url "") "/update-manifest.json")
          (current-update-public-key)
          #:key-id update-key-id
          #:maximum-bytes (* 4 1024 1024)))
       (define candidate (select-update (updater-config* (* 4 1024 1024)) manifest))
       (cond
         [candidate
          (set-box! candidate-box candidate)
          (define inner (update-candidate-manifest candidate))
          (state-set! 'phase "idle")
          (state-set! 'availableVersion (update-manifest-version inner))
          (hasheq 'status "available"
                  'currentVersion (effective-version)
                  'availableVersion (update-manifest-version inner)
                  'build (update-manifest-build inner)
                  'publishedAt (update-manifest-published-at inner)
                  'sizeBytes (update-artifact-size
                              (update-candidate-artifact candidate)))]
         [else
          (state-set! 'phase "idle")
          (state-set! 'availableVersion (void))
          (hasheq 'status "up-to-date"
                  'currentVersion (effective-version))])])))

;; ---------- download ----------

(define (artifact-file-name candidate)
  (define raw (update-artifact-url (update-candidate-artifact candidate)))
  (or (and (string? raw)
           (with-handlers ([exn:fail? (lambda (_) #f)])
             (let loop ([segments (url-path (string->url raw))] [seen #f])
               (cond
                 [(null? segments) seen]
                 [else
                  (define raw-seg (path/param-path (car segments)))
                  (loop (cdr segments)
                        (if (and (string? raw-seg) (not (string=? raw-seg "")))
                            raw-seg
                            seen))]))))
      "movebit-update"))

(define (copy-with-progress! in out total)
  (define buffer (make-bytes 65536))
  (let loop ([done 0] [last-percent -1])
    (define count (read-bytes-avail! buffer in))
    (cond
      [(eof-object? count) done]
      [else
       (write-bytes buffer out 0 count)
       (define next (+ done count))
       (when (> next maximum-download-bytes)
         (error 'download-update "update exceeds configured download limit"))
       (define percent
         (if (> total 0)
             (min 100 (quotient (* next 100) total))
             0))
       (when (> percent last-percent)
         (state-set! 'percent percent))
       (loop next percent)])))

(define (download-with-progress! candidate destination)
  (define artifact (update-candidate-artifact candidate))
  (define total (update-artifact-size artifact))
  (when (> total maximum-download-bytes)
    (error 'download-update "signed artifact size exceeds the download limit"))
  (make-parent-directory* destination)
  (define temporary (path-add-extension destination #".partial"))
  (when (file-exists? temporary) (delete-file temporary))
  (with-handlers ([exn:fail?
                   (lambda (e)
                     (when (file-exists? temporary) (delete-file temporary))
                     (raise e))])
    (define in
      (get-pure-port (string->url (update-artifact-url artifact))
                     '("User-Agent: MoveBit-Updater/1")
                     #:redirections 10))
    (dynamic-wind
      void
      (lambda ()
        (call-with-output-file temporary
          #:exists 'truncate/replace
          #:mode 'binary
          (lambda (out) (copy-with-progress! in out total))))
      (lambda () (close-input-port in)))
    ;; size + SHA-256 against the signed manifest before the file is trusted
    (verify-update-artifact! candidate temporary)
    (rename-file-or-directory temporary destination #t)
    destination))

(define (updates-dir data-dir)
  (build-path data-dir "updates"))

;; Runs on a backend worker thread; the host follows progress via the
;; update-state RPC. Never raises: failures surface through the phase.
(define (start-download! data-dir)
  (define worker (unbox worker-thread-box))
  (when (and worker (thread-running? worker))
    (error 'start-download! "an update download is already running"))
  (define candidate (unbox candidate-box))
  (unless candidate
    (error 'start-download! "no update is available; run a check first"))
  (state-set! 'phase "downloading")
  (state-set! 'percent 0)
  (state-set! 'message (void))
  (state-set! 'availableVersion
              (update-manifest-version (update-candidate-manifest candidate)))
  (define destination
    (build-path (updates-dir data-dir) (artifact-file-name candidate)))
  (set-box! worker-thread-box
            (thread
             (lambda ()
               (with-handlers
                   ([exn:fail?
                     (lambda (e)
                       (state-set! 'phase "error")
                       (state-set! 'message (exn-message e)))])
                 (define path (download-with-progress! candidate destination))
                 (state-set! 'phase "downloaded")
                 (state-set! 'percent 100)
                 (state-set! 'availableVersion
                             (update-manifest-version
                              (update-candidate-manifest candidate)))
                 (state-set! 'downloadedPath (path->string path)))))))

;; ---------- install preparation (Windows) ----------
;;
;; The verified zip IS the install layout. Extract it into
;; <data-dir>/updates/staged/ and write apply-update.cmd next to it: the
;; helper waits for the running app to exit, copies the staged payload over
;; the install directory, relaunches, and removes itself. Extract runs on
;; the caller's thread (the quit-and-install RPC worker): PowerShell's
;; Expand-Archive is built into Windows 10+.

(define (staged-dir data-dir)
  (build-path (updates-dir data-dir) "staged"))

(define (extract-zip! zip-path destination)
  (make-directory* destination)
  ;; Windows ships bsdtar since 1803 — it reads zip and is an order of
  ;; magnitude faster than Expand-Archive, whose per-file copy grinds
  ;; against real-time antivirus for minutes on a 300-file payload. The
  ;; child's output goes to a log FILE: a GUI process has no console, and a
  ;; subprocess inheriting the embedded runtime's dead stdout handle blocks
  ;; forever.
  (define log (path-add-extension destination #".extract.log"))
  (call-with-output-file log
    (lambda (out)
      (parameterize ([current-output-port out]
                     [current-error-port out])
        (unless (system*
                 (find-executable-path "tar")
                 "-xf" (path->string zip-path)
                 "-C" (path->string destination))
          (error 'extract-zip!
                 "tar extraction failed; see ~a" log))))
    #:exists 'replace)
  (unless (file-exists? (build-path destination "RivetHost.exe"))
    (error 'prepare-install!
           "staged payload is incomplete (no RivetHost.exe)")))

(define (write-apply-helper! data-dir pid)
  (define staged (staged-dir data-dir))
  ;; A trailing separator inside the quoted path reads as an escaped quote
  ;; in cmd (\"), mangling every argument after it — strip it.
  (define install
    (regexp-replace #rx"[/\\]+$" (path->string (current-install-dir)) ""))
  (define helper
    (build-path (updates-dir data-dir) "apply-update.cmd"))
  (define script
    (string-append
     "@echo off\r\n"
     "set TRACE=%~dp0apply-update-trace.txt\r\n"
     "echo [%date% %time%] helper started, waiting for PID "
     (number->string pid) " > \"%TRACE%\"\r\n"
     ":waitloop\r\n"
     "tasklist /FI \"PID eq " (number->string pid) "\" | find \""
     (number->string pid) "\" >nul\r\n"
     "if not errorlevel 1 (\r\n"
     "  timeout /t 1 /nobreak >nul\r\n"
     "  goto waitloop\r\n"
     ")\r\n"
     "echo [%time%] app exited, swapping payload >> \"%TRACE%\"\r\n"
     "robocopy \"" (path->string staged) "\" \""
     (path->string install) "\" /E /NFL /NDL /NJH /NJS >> \"%TRACE%\" 2>&1\r\n"
     "echo [%time%] robocopy exit %errorlevel% >> \"%TRACE%\"\r\n"
     "if errorlevel 8 goto :fail\r\n"
     "start \"\" \"" (path->string (build-path install "RivetHost.exe")) "\"\r\n"
     "echo [%time%] relaunched >> \"%TRACE%\"\r\n"
     "del \"%~f0\"\r\n"
     "exit /b 0\r\n"
     ":fail\r\n"
     "echo [%time%] FAILED >> \"%TRACE%\"\r\n"
     "exit /b 1\r\n"))
  (call-with-output-file helper
    (lambda (out) (display script out))
    #:exists 'replace)
  helper)

;; Verify the install directory is writable BEFORE the app exits — a pointer
;; to an unwritable directory would only fail inside the helper.
(define (assert-install-writable!)
  (define probe
    (build-path (current-install-dir) ".movebit-write-probe"))
  (call-with-output-file probe
    (lambda (out) (display "ok" out))
    #:exists 'replace)
  (delete-file probe))

;; Called from the quit-and-install RPC on its worker: extract the verified
;; zip, write the helper, and hand back the command to spawn. The host then
;; exits and the helper finishes the swap.
(define (prepare-install! data-dir)
  (define downloaded
    (hash-ref (update-state-snapshot) 'downloadedPath #f))
  (unless (and downloaded (file-exists? downloaded))
    (error 'prepare-install! "no downloaded update; run a download first"))
  (assert-install-writable!)
  (define staged (staged-dir data-dir))
  (when (directory-exists? staged)
    (delete-directory/files staged))
  (extract-zip! (string->path downloaded) staged)
  (define helper (write-apply-helper! data-dir (getpid)))
  (state-set! 'phase "ready-to-apply")
  (state-set! 'stagedPath (path->string staged))
  (path->string helper))

;; The apply helper waits on the precise PID of the exiting app, so a
;; stray RivetHost.exe from another install can never hold the swap open.
(define getpid
  (case (system-type 'os)
    [(windows)
     (define-ffi-definer define-kernel32 (ffi-lib "kernel32"))
     (define-kernel32 GetCurrentProcessId (_fun -> _uint32))
     (lambda () (GetCurrentProcessId))]
    [else
     ;; Install preparation is a Windows flow; other platforms get their
     ;; own install step when their hosts adopt the updater.
     (lambda () 0)]))
