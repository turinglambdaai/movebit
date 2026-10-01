#lang racket/base

;; Platform data-directory helper. The contract (kept identical to the old app
;; for drop-in migration): config.json / history.json live under
;;   macOS   ~/Library/Application Support/movebit
;;   Linux   ~/.config/movebit            (XDG_CONFIG_HOME respected)
;;   Windows %APPDATA%/movebit
;; MOVEBIT_DATA_DIR overrides everything for tests and embedded hosts.

(require racket/path)

(provide default-data-dir
         movebit-data-dir)

(define (home-dir)
  (find-system-path 'home-dir))

(define (default-data-dir)
  (case (system-type)
    [(macosx)
     (build-path (home-dir) "Library" "Application Support" "movebit")]
    [(windows)
     (define appdata (getenv "APPDATA"))
     (build-path (if (and appdata (not (string=? appdata "")))
                     (string->path appdata)
                     (build-path (home-dir) "AppData" "Roaming"))
                 "movebit")]
    [else
     (define xdg (getenv "XDG_CONFIG_HOME"))
     (build-path (if (and xdg (not (string=? xdg "")) (absolute-path? xdg))
                     (string->path xdg)
                     (build-path (home-dir) ".config"))
                 "movebit")]))

(define (movebit-data-dir)
  (define override (getenv "MOVEBIT_DATA_DIR"))
  (if (and override (not (string=? override "")))
      (simple-form-path override)
      (default-data-dir)))
