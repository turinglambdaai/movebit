# macOS character-style health reminder preview (developer-only)

**Update for v1 reboot (#55):** This experiment is an input-safe **water/stand-up/eye-rest reminder renderer**, not a separate desktop-pet product. v0.x had no users; no legacy migration or force-cover preservation is required for v1. This prototype still uses the old backend until the semantic reminder engine replacement lands.

Parent product task: [#51](https://github.com/turinglambdaai/movebit/issues/51). Depends on cue foundation PR #52. This slice is a **native Mac proof of rendering and non-interception**, not the cross-platform pet release. **MoveBit remains a hydration, movement and eye-rest reminder product**. This character is only a visual way of presenting the existing due reminders; it does not add autonomous pet prompts, care needs or a gameplay loop.

## Run

On macOS with the documented Rivet toolchain installed, run the development host with:

```sh
MOVEBIT_COMPANION_PREVIEW=1 raco rivet dev
```

The app's normal reminders are unchanged unless this environment variable is set at launch. The packaged acceptance flow (`MOVEBIT_ACCEPTANCE=1`) explicitly ignores the preview flag and continues to validate the shipping UI.

### Preview behavior

- Health gestures now include a **short readable localized advice label** so the character is not just an ambiguous icon. While a cue is outstanding, the macOS tray exposes **Got it** and **Later** for all outstanding merged kinds (the same current backend snooze RPC as the old toast). Got it means self-reported acknowledgement only; Later is currently **best-effort** (no durable acknowledgement/response RPC yet). These limitations must be resolved in v1 backend #55 before the prototype is shipped.

- Tiny original vector pet, anchored at the lower-right of the main screen's **visible** work area (without traversing screen center).
- Its entire non-activating floating window uses AppKit `ignoresMouseEvents = true`. **No mouse drag/click is implemented in this preview**; that must wait for a proper, tested input-region implementation. Clicking through the pet should reach underlying windows.
- One gesture for each already-merged `RemindersDue`: sit > water > micro, with no extra scheduler or RPC. Displaying the cue does not acknowledge, snooze or reset the backend.
- A maximum of two hops when a cue arrives, then return to a static idle appearance after seven seconds. macOS Reduce Motion disables hops.
- Status-bar menu gets a preview-only `Show/Hide preview pet` toggle. Toggle is entirely local and immediate even if RPC is disconnected. Hiding preview restores legacy reminders rather than losing them.
- The pet hides during full-screen forced breaks, and a normal forced-break emergency exit retains priority. Onboarding, acceptance, screen removal and Quit paths never intentionally block user input.
- No added account, recording permissions, network traffic or borrowed image assets.

## Manual native acceptance (REQUIRED before calling this validated)

- [ ] Startup without flag: no pet, legacy toast/micro/force-break behavior unchanged.
- [ ] With flag: pet appears near visible lower-right edge; users can click, select text, type, drag application UI and use underlying controls through its full 98×98 bounding box. Foreground app never loses focus on appearance/reminder.
- [ ] Send synthetic water, micro, sit, and merged reminders from backend fixture; exactly one cue; no duplicate popup or changed history counters.
- [ ] Status-bar Hide works while backend is unresponsive; the pet disappears immediately; legacy reminders are available. Show restores idle.
- [ ] Change monitor count, primary display, scale and work-area/dock; pet remains on-screen.
- [ ] Move another app into macOS fullscreen and try presentations/screen sharing. **Known limitation:** the preview has no robust fullscreen/screen-sharing detection. It deliberately does *not* join all Spaces, but this is not proof of private/presentation-safe behavior. Manual Hide is required.
- [ ] Trigger forced rest with explicit strong-mode opt-in; preview hides and cannot interfere with emergency exit.
- [ ] Enable Reduce Motion; no hop animation; test VoiceOver.
- [ ] Disable environment flag and relaunch: no residual config change.

## Technical limitations / next work

This is **not** an interactive pet and has no localized preview-only menu labels, pet persistence, stable multi-monitor placement preference, screen-share detection, native accessibility acceptance or host parity. It is intentionally opt-in via an environment flag, not settings or onboarding. A fully mouse-through panel cannot expose pet click/drag actions; those need independently proven native hit regions and explicit reversible controls.

Do not make this the default, change public README claims, or merge without native macOS build + packaged UI acceptance evidence. Next vertical slices: input regions, Focus/Presentation mode, persistence/migration, cue/snooze interaction, Windows and GTK/Wayland fallbacks.
