#lang racket/base

;; Wayland idle detection without compositor-specific native libraries, port
;; of Services/LinuxWaylandIdleProvider.cs.
;;
;; GNOME/Mutter exposes the same precise idle counter it uses internally
;; through org.gnome.Mutter.IdleMonitor. Some other desktops expose the
;; freedesktop ScreenSaver GetSessionIdleTime method. We query those
;; session-bus APIs through busctl/gdbus, cache the first backend that works,
;; and fail closed (#f) when the compositor exposes no trustworthy counter.

(require racket/format
         racket/match
         racket/port
         racket/string)

(provide make-idle-provider
         parse-first-unsigned
         query-timeout-ms)

(define query-timeout-ms 750)

(struct backend (name tool args scale) #:transparent)

(define backends
  (list
   (backend 'mutter-busctl
            "busctl"
            '("--user" "call"
              "org.gnome.Mutter.IdleMonitor"
              "/org/gnome/Mutter/IdleMonitor/Core"
              "org.gnome.Mutter.IdleMonitor"
              "GetIdletime")
            1.0)
   (backend 'mutter-gdbus
            "gdbus"
            '("call" "--session"
              "--dest" "org.gnome.Mutter.IdleMonitor"
              "--object-path" "/org/gnome/Mutter/IdleMonitor/Core"
              "--method" "org.gnome.Mutter.IdleMonitor.GetIdletime")
            1.0)
   (backend 'screensaver-busctl
            "busctl"
            '("--user" "call"
              "org.freedesktop.ScreenSaver"
              "/ScreenSaver"
              "org.freedesktop.ScreenSaver"
              "GetSessionIdleTime")
            1000.0) ; ScreenSaver API reports seconds
   (backend 'screensaver-gdbus
            "gdbus"
            '("call" "--session"
              "--dest" "org.freedesktop.ScreenSaver"
              "--object-path" "/ScreenSaver"
              "--method" "org.freedesktop.ScreenSaver.GetSessionIdleTime")
            1000.0)))

;; First standalone unsigned number in a D-Bus payload. A number embedded in a
;; larger alphanumeric token ("uint64 nope") must NOT match, mirroring the C#
;; regex (?<![A-Za-z0-9])\d+(?![A-Za-z0-9]).
(define (parse-first-unsigned output)
  (and (string? output)
       (not (string=? output ""))
       (let/ec return
         (for ([m (in-list (regexp-match-positions* #rx"[0-9]+" output))])
           (define start (car m))
           (define end (cdr m))
           (define before (if (zero? start) #f (string-ref output (- start 1))))
           (define after (if (= end (string-length output)) #f (string-ref output end)))
           (define (alphanumeric? ch)
             (and ch (or (char-alphabetic? ch) (char-numeric? ch))))
           (when (and (not (alphanumeric? before))
                      (not (alphanumeric? after)))
             (return (string->number (substring output start end)))))
         #f)))

;; Run one query with a hard timeout; kill the process on overrun.
(define (run-command executable args)
  (with-handlers ([exn:fail? (lambda (_) #f)])
    (define-values (proc stdout stdin stderr)
      (apply subprocess #f #f #f executable (map ~a args)))
    (define done (sync/timeout (/ query-timeout-ms 1000.0) proc))
    (unless done (subprocess-kill proc #t))
    (define output
      (with-handlers ([exn:fail? (lambda (_) "")])
        (string-trim (port->string stdout))))
    (close-input-port stdout)
    (close-input-port stderr)
    (close-output-port stdin)
    (and done
         (let ([code (subprocess-status proc)])
           (and (exact-integer? code)
                (zero? code)
                (unless-zero (string-length output) output))))))

(define (unless-zero n value)
  (if (zero? n) #f value))

;; Cache of the first backend that answered; #f = probe needed, 'unsupported =
;; probed and nothing worked. Session services can restart after shell/compositor
;; upgrades, so a cached backend that starts failing re-probes once per call.
(define cached-backend (box #f))

(define (try-read backend-entry)
  (define output (run-command (backend-tool backend-entry) (backend-args backend-entry)))
  (define raw (parse-first-unsigned output))
  (and raw
       (let ([ms (* raw (backend-scale backend-entry))])
         (and (>= ms 0) (/ ms 1000.0)))))

(define (make-idle-provider)
  (lambda ()
    (define current (unbox cached-backend))
    (define result
      (cond
        [(and current (not (eq? current 'unsupported)))
         (or (try-read current)
             (begin (set-box! cached-backend #f) #f))]
        [(eq? current 'unsupported) #f]
        [else
         (let/ec return
           (for ([candidate (in-list backends)])
             (define idle (try-read candidate))
             (when idle
               (set-box! cached-backend candidate)
               (return idle)))
           (set-box! cached-backend 'unsupported)
           #f)]))
    result))
