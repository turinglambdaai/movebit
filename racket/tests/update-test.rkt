#lang racket/base

;; Update-flow unit tests (racket/movebit/update.rkt): the pure pieces of the
;; P0 host wiring — the AppImage sibling-asset derivation — plus the state
;; box contract the hosts poll through get-update-state. Network paths are
;; covered by the release pipeline, not here.

(require rackunit
         "../movebit/update.rkt")

;; The feed's linux entry and the sibling .AppImage release asset share the
;; release-contract arch spelling (x64|arm64), so the derivation is only the
;; extension swap.
(check-equal? (appimage-asset-name "movebit-0.3.1-linux-x64.tar.gz")
              "movebit-0.3.1-linux-x64.AppImage")
(check-equal? (appimage-asset-name "movebit-1.0.0-linux-arm64.tar.gz")
              "movebit-1.0.0-linux-arm64.AppImage")

;; A non-tar.gz feed entry cannot derive a sibling — refuse loudly.
(check-exn exn:fail?
           (lambda () (appimage-asset-name "movebit-0.3.1-windows-x64.zip")))

;; The state box starts idle and reset returns it there: the hosts' poll
;; loop relies on these exact keys (phase/percent/message/availableVersion/
;; downloadedPath) surviving every transition.
(reset-update-state!)
(define snapshot (update-state-snapshot))
(check-equal? (hash-ref snapshot 'phase) "idle")
(check-equal? (hash-ref snapshot 'percent) 0)
(check-false (hash-ref snapshot 'availableVersion))
(check-false (hash-ref snapshot 'downloadedPath))
