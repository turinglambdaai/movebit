#lang racket/base

;; Server-level test over a REAL RVT1 server (same shape as the rivet
;; taskboard example). It proves the Rivet threading model claim the backend
;; depends on: `current-event-emitter` lives on the server reader thread, so a
;; module-top-level thread has NO emitter — but the tick thread started inside
;; the `init` RPC inherits it and its events reach the client. Also exercises
;; complete-break / snooze / set-config / get-history / autostart over the wire.
;;
;; The virtual clock advances 60 s of work time per `now` read, so one 0.05 s
;; tick accumulates one active minute and a 10-minute sit cycle fires after
;; ~10 ticks — no real waiting.

(require rackunit
         racket/async-channel
         racket/bool
         racket/file
         racket/runtime-path
         racket/string
         rivet/backend
         rivet/protocol
         rivet/system
         json
         "../../app/backend.rkt"
         "../movebit/idle.rkt"
         "../movebit/timeutil.rkt")

;; --- fake system adapter (autostart over the wire) -------------------------

(define autostart-box (box #f))

(define (unimpl who) (lambda args (error who "unavailable in test adapter")))

(define fake-adapter
  (system-adapter 'fake '(autostart)
                  (unimpl 'acquire-single-instance!)
                  (unimpl 'register-activation-handler!)
                  (unimpl 'show-system-notification!)
                  (unimpl 'set-tray-menu!)
                  (lambda (enabled?) (set-box! autostart-box enabled?))
                  (lambda () (unbox autostart-box))
                  (unimpl 'secure-store-set!)
                  (unimpl 'secure-store-ref)
                  (unimpl 'secure-store-remove!)
                  (unimpl 'install-crash-hook!)))

;; --- temp data dir with a fast config --------------------------------------

(define tmp-dir (make-temporary-file "movebit-server-test-~a" 'directory))
(call-with-output-file (build-path tmp-dir "config.json")
  (lambda (out)
    (write-json
     (hasheq 'SitReminderMinutes 10
             'WaterReminderMinutes 5
             'AwayResetMinutes 1
             'ForceBreakEnabled #t
             'BreakDurationMinutes 5
             'SkipAfterSeconds 20
             'SnoozeMinutes 10
             'MicroBreakEnabled #f)
     out)))

;; --- virtual clock: +60 s of active time per read ---------------------------

(define clock (box (local-ymd->ms 2026 1 1 9 0 0)))
(define (virtual-now-ms)
  (define v (+ (unbox clock) 60000))
  (set-box! clock v)
  v)

;; --- start the server under the injected seams ------------------------------

(define-values (server-in client-out) (make-pipe))
(define-values (client-in server-out) (make-pipe))

(define server-thread
  (parameterize ([current-data-dir tmp-dir]
                 [current-now-ms virtual-now-ms]
                 [current-idle-provider
                  (lambda () (make-constant-idle-provider 0))] ; always active
                 [current-tick-interval-seconds 0.05]
                 [current-system-adapter fake-adapter])
    (thread (lambda () (serve server-in server-out)))))

(define hello (read-frame client-in))
(check-equal? (frame-type hello) message:hello)

;; frame pump with deadlines so a regression fails instead of hanging
(define frames (make-async-channel 1024))
(define reader-thread
  (thread (lambda ()
            (let loop ()
              (define f (read-frame client-in))
              (unless (eof-object? f)
                (async-channel-put frames f)
                (loop))))))

(define (next-frame [timeout 10.0])
  (define f (sync/timeout timeout frames))
  (unless f (error 'server-test "timed out waiting for a frame"))
  f)

(define next-id 1)

(define (send-request name . args)
  (define id next-id)
  (set! next-id (add1 next-id))
  (write-frame (frame message:request id (encode-value (cons name args)))
               client-out)
  id)

;; Call an RPC; returns (values result events-seen-while-waiting).
(define (call name . args)
  (define id (apply send-request name args))
  (let loop ([events '()])
    (define f (next-frame))
    (cond
      [(= (frame-type f) message:event)
       (loop (cons (decode-value (frame-payload f)) events))]
      [(= (frame-id f) id)
       (check-equal? (frame-type f) message:response)
       (values (decode-value (frame-payload f)) (reverse events))]
      [else (loop events)])))

;; Wait for the next event with the given name, skipping everything else.
(define (wait-for-event name [timeout 10.0])
  (let loop ()
    (define f (next-frame timeout))
    (define payload (decode-value (frame-payload f)))
    (if (and (= (frame-type f) message:event)
             (string=? (car payload) name))
        payload
        (loop))))

;; --- 1. init starts the lazy tick thread -----------------------------------

(define-values (init-result init-events) (call "init"))
(check-true (void? init-result))

;; The tick thread (inside the init RPC) emits tick-stats State updates and
;; events. Wait for the forced break: sit fires after 10 active minutes.

(define break-started (wait-for-event "break-started"))
(define bs (list-ref break-started 1))
(check-equal? bs (list (* 5 60 1000) (* 20 1000))) ; duration 5 min, skip 20 s

;; --- 2. complete-break over the wire ---------------------------------------

(define-values (cb-result cb-events)
  (call "complete-break"))
(check-true (void? cb-result))
(check-true
 (for/or ([e (in-list cb-events)])
   (and (string=? (car e) "break-ended")
        (equal? (list-ref e 1) (list #t))))
 "break-ended(completed #t) must arrive with the complete-break response")

;; Today's state shows one counted sit break (the swallowed kind still counted).
(define-values (today-wire today-ev) (call "$state/get" "today"))
;; today wire: (date active-mins sit-breaks water-reminders micro-breaks longest)
(check-true (string=? (list-ref today-wire 0) (ms->date-key (unbox clock))))
(check-equal? (list-ref today-wire 2) 1)
(check-true (>= (list-ref today-wire 1) 10))

;; --- 3. snooze over the wire ------------------------------------------------

;; Water fires every 5 active minutes; wait for the merged-toast event.

(define reminders-due (wait-for-event "reminders-due"))
(define rd (list-ref reminders-due 1))
(check-equal? rd (list (list "water") #f)) ; single kind -> not merged

(define-values (snooze-result snooze-ev) (call "snooze" "water"))
(check-true (void? snooze-result))

;; Snooze is NOT a delay: water accumulator = interval - snooze = 5 - 10 = -5.
(define-values (loops-wire loops-ev) (call "$state/get" "loops"))
(define water-loop
  (findf (lambda (l) (string=? (list-ref l 0) "water")) loops-wire))
(check-true (pair? water-loop))
(check-equal? (list-ref water-loop 2) -5)                 ; accumulated-mins
(check-equal? (list-ref water-loop 1) 10)                 ; due-in = 5 - (-5)

;; --- 4. set-config clamps + announces ---------------------------------------

(define-values (cfg-result cfg-events)
  (call "set-config" (list 5 999 0 #t 99 999 999 #f 99 99 #t #t #f "klingon")))
(check-true (void? cfg-result))
(define config-event
  (for/first ([e (in-list cfg-events)] #:when (string=? (car e) "config-changed"))
    e))
(check-true (pair? config-event))
(define saved-config (list-ref config-event 1))
(check-equal? (list-ref saved-config 0) 10)    ; sit clamped
(check-equal? (list-ref saved-config 1) 180)   ; water clamped
(check-equal? (list-ref saved-config 13) "auto") ; language normalized

;; --- 5. get-history zero-fills gaps -----------------------------------------


;; Let the periodic ~10-tick stats flush fire (ticks run every 50 ms).
(sleep 1.0)

(define-values (history-wire hist-ev) (call "get-history" 7))
(check-equal? (length history-wire) 7)
(check-true (>= (list-ref (list-ref history-wire 6) 1) 10)) ; today active-mins
(check-true (zero? (list-ref (list-ref history-wire 0) 1))) ; oldest is a hole

;; --- 6. autostart delegates to the system adapter ---------------------------

(define-values (autostart-before asb-ev) (call "get-autostart"))
(check-false autostart-before)
(define-values (set-autostart-result as-set-ev) (call "set-autostart" #t))
(check-true (void? set-autostart-result))
(check-true (unbox autostart-box))
(define-values (autostart-after asa-ev) (call "get-autostart"))
(check-true autostart-after)

;; --- 7. diagnostics + flush-now touch real behavior -------------------------

(define-values (diag diag-ev) (call "get-diagnostics"))
(check-true (string-contains? diag "movebit"))
(check-true (string-contains? diag "idle-provider: constant"))

(define-values (flush-result flush-ev) (call "flush-now"))
(check-true (void? flush-result))
(check-true (file-exists? (build-path tmp-dir "history.json"))
            "flush-now must persist today's stats to history.json")

;; The persisted config is the clamped one (immediate auto-save on set-config).
(define persisted-config
  (call-with-input-file (build-path tmp-dir "config.json") read-json))
(check-equal? (hash-ref persisted-config 'WaterReminderMinutes) 180)

;; --- shutdown ----------------------------------------------------------------

(write-frame (frame message:shutdown 0 #"") client-out)
(thread-wait server-thread)

(delete-directory/files tmp-dir)
