#lang racket/base

;; Login autostart, port of the old Services/AutoStart.cs — a health tool whose
;; whole value is "always running". Per-platform targets:
;;   macOS   ~/Library/LaunchAgents/com.turinglambdaai.movebit.plist (RunAtLoad)
;;   Linux   $XDG_CONFIG_HOME/autostart/movebit.desktop
;;   Windows HKCU\Software\Microsoft\Windows\CurrentVersion\Run, value "MoveBit"
;; Best-effort exactly like the old app: an enabled check never throws and a
;; failed toggle is swallowed — the app keeps running either way. The exe path
;; and home directory are parameters so tests can run isolated.

(require racket/file
         racket/path
         racket/port
         racket/runtime-path
         racket/string
         (only-in racket/format ~a))

(provide autostart-enabled?
         set-autostart!
         ;; test hooks
         autostart-home
         autostart-executable-path)

(define autostart-home
  (make-parameter (find-system-path 'home-dir)))

;; Process-global override for tests and embedded hosts (same pattern as
;; MOVEBIT_DATA_DIR): env wins over the parameter so every thread sees it.
(define (autostart-base-dir)
  (define override (getenv "MOVEBIT_AUTOSTART_HOME"))
  (if (and override (not (string=? override "")))
      (string->path override)
      (autostart-home)))

;; Defaults to the host executable the embedded runtime was booted with
;; (rivet passes the host binary as boot.exec_file). Tests override this.
(define autostart-executable-path
  (make-parameter
   (path->string (find-system-path 'exec-file))))

(define (launch-agent-path)
  (build-path (autostart-base-dir) "Library" "LaunchAgents"
              "com.turinglambdaai.movebit.plist"))

(define (xdg-autostart-dir)
  (define xdg (getenv "XDG_CONFIG_HOME"))
  (build-path (if (and xdg (not (string=? xdg "")) (absolute-path? xdg))
                  (string->path xdg)
                  (build-path (autostart-base-dir) ".config"))
              "autostart"))

(define (desktop-entry-path)
  (build-path (xdg-autostart-dir) "movebit.desktop"))

(define (windows-run-key)
  "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run")

;; --- enabled? --------------------------------------------------------------

(define (autostart-enabled?)
  (with-handlers ([exn:fail? (lambda (_) #f)])
    (case (system-type)
      [(windows) (windows-value-present?)]
      [(macosx) (file-exists? (launch-agent-path))]
      [else (file-exists? (desktop-entry-path))])))

;; --- set! ------------------------------------------------------------------

(define (set-autostart! enabled?)
  (with-handlers ([exn:fail? void])
    (case (system-type)
      [(windows) (windows-set! enabled?)]
      [(macosx) (macos-set! enabled?)]
      [else (linux-set! enabled?)])))

;; --- macOS LaunchAgent -----------------------------------------------------

(define (macos-set! enable?)
  (define plist (launch-agent-path))
  (if enable?
      (begin
        (make-directory* (path-only plist))
        (with-output-to-file plist #:exists 'replace
          (lambda ()
            (displayln "<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            (displayln "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \
\"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">")
            (displayln "<plist version=\"1.0\">")
            (displayln "<dict>")
            (displayln "  <key>Label</key><string>com.turinglambdaai.movebit</string>")
            (displayln "  <key>ProgramArguments</key>")
            (displayln (~a "  <array><string>" (xml-escape (autostart-executable-path))
                           "</string></array>"))
            (displayln "  <key>RunAtLoad</key><true/>")
            (displayln "</dict>")
            (display "</plist>"))))
      (when (file-exists? plist)
        (delete-file plist))))

(define (xml-escape s)
  (string-append*
   (for/list ([c (in-string s)])
     (case c
       [(#\<) "&lt;"]
       [(#\>) "&gt;"]
       [(#\&) "&amp;"]
       [else (string c)]))))

;; --- Linux XDG autostart ---------------------------------------------------

(define (linux-set! enable?)
  (define entry (desktop-entry-path))
  (if enable?
      (begin
        (make-directory* (path-only entry))
        (with-output-to-file entry #:exists 'replace
          (lambda ()
            (displayln "[Desktop Entry]")
            (displayln "Type=Application")
            (displayln "Name=MoveBit")
            (displayln (~a "Exec=" (quote-desktop-exec (autostart-executable-path))))
            (display "X-GNOME-Autostart-enabled=true"))))
      (when (file-exists? entry)
        (delete-file entry))))

;; Desktop Entry Exec quoting, port of QuoteDesktopExec: double quotes with
;; backslash-escaped backslash, double quote, backtick, and dollar sign.
(define (quote-desktop-exec value)
  (string-append
   "\""
   (string-append*
    (for/list ([c (in-string value)])
      (case c
        [(#\\) "\\\\"]
        [(#\") "\\\""]
        [(#\`) "\\`"]
        [(#\$) "\\$"]
        [else (string c)])))
   "\""))

;; --- Windows HKCU Run key --------------------------------------------------
;; Implemented in autostart-registry.rkt (advapi32 FFI), loaded lazily so the
;; ffi-lib call never runs on macOS/Linux.

(define-runtime-path registry-module "autostart-registry.rkt")

(define (registry-fun name)
  (dynamic-require registry-module name))

(define (windows-value-present?)
  (with-handlers ([exn:fail? (lambda (_) #f)])
    ((registry-fun 'registry-value-present?))))

(define (windows-set! enable?)
  (if enable?
      ((registry-fun 'registry-set-value!) (autostart-executable-path))
      ((registry-fun 'registry-delete-value!))))
