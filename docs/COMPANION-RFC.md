# Health reminder UX contract — companion-style cues (RFC / implementation baseline)

Status: **implementation direction approved; native companion NOT shipped**. Product implementation tracker: [#51](https://github.com/turinglambdaai/movebit/issues/51). Related user-control fix: [#49](https://github.com/turinglambdaai/movebit/issues/49), [#50](https://github.com/turinglambdaai/movebit/pull/50).

[中文](COMPANION-RFC.zh-CN.md) · English

## Product purpose: reminders first, character second

**MoveBit remains a hydration, stand-up and eye-rest reminder app.** The scheduler's active-time detection, per-kind reminder cadence, snooze, pause, history and statistics are the product. A friendly animated character is a **presentation interface** that makes a due reminder easier to notice and more pleasant to act on; it is not a standalone desktop pet, virtual friend or pet-care game.

**North-star:** deliver timely, recognizable, actionable water/sit/micro cues **without blocking work**. Prioritize accurate timing and clear actions first, non-interference and user control second, charm third. Do not introduce autonomous attention-seeking cues that are unrelated to health reminders. A cute animation is never evidence that the user has actually drank water or rested.

**A representative moment:** when the **existing water reminder** becomes due, a small original character near the screen edge holds up a cup, optionally with a clear short message. The user can acknowledge, snooze, or hide it without losing focus. The cue communicates an existing health reminder, not a request to interact with a pet.

## Non-negotiable design rules

1. **User stays in control.** Companion is opt-in for existing installs; gentle reminders remain available. The persisted strong-break preference is left alone; forced fullscreen break stays separately opt-in with the unconditional emergency exit.
2. **Zero interaction interception outside the pet.** No invisible input-taking rectangular overlay, no keyboard focus theft; native pointer hit-testing or explicit fallback. User can hide/quit even when the backend cannot respond.
3. **Edge-docked by default.** Movement is small and bounded. Never cross central content autonomously during normal work. Presentation/Focus mode stops movement and cueing; automated suppression must be based on actually available OS signals, backed by manual control.
4. **One reminder moment, one cue.** The Racket orchestrator already coalesces due sit, water and micro events; a single cue may retain multiple kinds for correct snooze semantics. Do not create extra reminders from the animation lifecycle.
5. **No diversion into pet software.** Ambient motion is optional and subordinate to actual reminders. No separate play loops, feeding, rewards, pets needing attention, repeated begging, negative moods from missed reminders, punishment, scores, artificial dependence or unsupported health claims.
6. **Accessibility.** Respect Reduce Motion, give keyboard and assistive-tech users equivalent actions, support multilingual content, and record native platform behavior rather than asserting parity without tests.

## First pet and gesture inventory

| Existing reminder | Pet gesture (original assets only) | Controls |
| --- | --- | --- |
| Water | Holds a tiny cup, briefly glances up | Done · Later · Hide |
| Micro | Blink/stretch/look far away | Done · Later · Hide |
| Sit | Stands up/invites a stretch | Done · Later · Hide |
| Merged event | One combined gesture; preserve all due kinds | All relevant due kinds handled together |
| No reminder | Quiet by default; optional sparse subtle idle motion | Focus · Hide; optional reposition |

Modes for MVP: `idle`, `water`, `stretch` (sit), `blink` (micro), `quiet`, `static` (Reduce Motion); optional short celebratory acknowledgement. No roaming, pet mood system or play mode in the reminder MVP. Animations are a **visual expression of backend-owned reminders**, not a second source of health events.

## Reminder semantics and product success

- `Due` is determined by the existing backend scheduler. `Shown` means a cue was actually displayed; `Acknowledged` means the user responded; `Snoozed` means the backend defers the kind(s) by its configured period. **Acknowledged ≠ verified drinking or physical break.** 
- Preserve every kind in a merged event and its correct `Later` mapping. Hiding/suppressing a cue must not fabricate a successful health action, count a completion, change scheduled periods or silently lose an overdue reminder; choose and test explicit deferral/expiry semantics.
- The visual cue must clearly communicate **what action is being suggested**: drink water, stand/move, or look away from the screen. A cute unexplained symbol is insufficient.
- Compare against the legacy gentle-toast baseline on reminder recognition, user-reported willingness to act, correct Done/Later semantics, nuisance/disable rates, and interruption/focus errors. Delight and duration of interaction are **secondary** indicators.

## Engineering boundaries

- Shared `racket/movebit/` domain logic owns cycle timing, coalescing, silence/presentation policy and allowed cue intent; no new UI-only health scheduler.
- The first foundation is `racket/movebit/companion-cue.rkt`, a **pure, currently unwired** intent mapper from already-merged kinds to `legacy | quiet | companion` delivery and gesture. Default = legacy. Tests pin deterministic merging and non-animated silent/reduced-motion modes. It does **not** implement cue cooldowns, detection or presentation reliability yet.
- `macos-host/`, `windows/`, `linux/` implement local native pet surfaces, input regions/transparent hit-testing, docking, pointer drag and animations. Each platform must document reliable capabilities; **do not pretend Wayland can position arbitrary top-level windows or expose reliable system-wide hit testing**.
- Existing `rivet-schema.json`, generated clients, user config/history and release acceptance remain the single authoritative contract. Additive RPC changes must be coordinated across three hosts, generated clients and wire/schema tests.
- No new screen-recording/accessibility permission request to detect meetings. Privacy stays local. Degrade to a small static companion/tray or the existing gentle toast if safe overlay behavior is unavailable.
- Both native old UX and new companion must be selectable until migration is verified, including app upgrades.

## Delivery / verification

Implementation is sliced and tracked in [#51](https://github.com/turinglambdaai/movebit/issues/51). Native work begins with **correct, usable water/sit/micro reminders and input safety**, not collectible pets or elaborate animations. Verify on all supported hosts and packaged builds: typing/clicking/selection underneath the pet, focus restoration, changing displays, OS scaling, sleep/wake, fullscreen/presentation, Reduce Motion, keyboard/readers, backend crash, snooze/away-reset and explicit opt-out.

Use `raco test racket/`, wire/schema checks and `raco rivet acceptance --json` (packaged app, native UI). Report actual measured results and platform gaps; tests passing on one host do not prove parity. Keep public docs in English and Chinese.
