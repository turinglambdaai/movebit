import SwiftUI
import AppKit
import RivetEmbedding
import RivetRuntime
import RivetSystem

// Tray app with no SwiftUI scenes: every window is an AppKit NSWindow owned
// by the delegate. Scene placeholders are not viable here — on current macOS
// both Settings and Window scenes present their (empty) window at launch,
// which users read as a stray "MoveBit Settings" window that never goes away.
@main
enum MoveBitApp {
    @MainActor
    static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        AppDelegate.shared = delegate // NSApplication.delegate is not retained
        app.delegate = delegate
        app.setActivationPolicy(.accessory)
        app.run()
    }
}

/// The droplet status-bar glyph, drawn as a template image (pure black +
/// alpha) so it follows the menu bar's light/dark appearance. Drawn in code —
/// SwiftPM resource bundles cannot ship inside a signed rivet app bundle.
private enum MenuBarIcon {
    static var droplet: NSImage {
        let image = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { _ in
            let path = NSBezierPath()
            path.move(to: NSPoint(x: 9, y: 16.5))
            path.curve(to: NSPoint(x: 3.1, y: 6.7),
                       controlPoint1: NSPoint(x: 9, y: 13.2),
                       controlPoint2: NSPoint(x: 4.6, y: 10.9))
            path.appendArc(withCenter: NSPoint(x: 9, y: 6.7), radius: 5.9,
                           startAngle: 180, endAngle: 360, clockwise: false)
            path.curve(to: NSPoint(x: 9, y: 16.5),
                       controlPoint1: NSPoint(x: 13.4, y: 10.9),
                       controlPoint2: NSPoint(x: 9, y: 13.2))
            path.close()
            NSColor.black.setFill()
            path.fill()
            return true
        }
        image.isTemplate = true
        return image
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    nonisolated(unsafe) static var shared: AppDelegate?

    private var mainWindow: NSWindow?
    private var onboardingWindow: NSWindow?
    private var onboardingState: OnboardingState?
    private var menuBar: RivetMenuBarController?
    private var overlayController: BreakOverlayController?
    private var toastController: ToastPanelController?
    private var microController: MicroPanelController?
    // Explicit developer-only preview, not a changed default or migration.
    // Packaging acceptance deliberately continues exercising the real legacy UI.
    private let companionPreviewEnabled =
        ProcessInfo.processInfo.environment["MOVEBIT_COMPANION_PREVIEW"] == "1" &&
        ProcessInfo.processInfo.environment["MOVEBIT_ACCEPTANCE"] != "1"
    private var companionPreviewHidden = false
    private var companionPreviewController: CompanionPreviewController?
    // Retained for the process lifetime: dropping it releases the lease.
    private var instanceLease: RivetSingleInstance?
    private(set) var model: MoveBitModel?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Second launches exit; the running instance keeps the tray.
        if let lease = try? RivetSingleInstance(applicationID: "site.jrtx.movebit") {
            guard lease.isPrimary else {
                NSApp.terminate(nil)
                return
            }
            instanceLease = lease
        }
        Self.shared = self
        installMenus()

        let model = MoveBitModel()
        self.model = model

        model.onReady = { [weak self] in self?.handleReady() }
        model.onConfigChanged = { [weak self] in self?.rebuildMenuBar() }
        model.onRemindersDue = { [weak self] due in self?.handleRemindersDue(due) }
        model.onBreakStarted = { [weak self] in self?.handleBreakStarted() }
        model.onBreakEnded = { [weak self] completed in self?.handleBreakEnded(completed: completed) }
        // Restart-install handoff: the backend staged the update and spawned
        // the apply helper; quitting lets it swap the bundle and relaunch.
        model.onQuitRequested = { [weak self] in self?.quit() }

        installOverlayControllers(model: model)
        rebuildMenuBar()
        installMainWindow(model: model)

        model.start()
    }

    func applicationSupportsSecureRestorableState(_ app: NSApplication) -> Bool { true }

    func applicationWillTerminate(_ notification: Notification) {
        model?.flushNowSync()
    }

    // MARK: launch

    private func handleReady() {
        guard let model else { return }
        if ProcessInfo.processInfo.environment["MOVEBIT_ACCEPTANCE"] == "1" {
            showMainWindow()
            return
        }
        if !model.config.welcome_shown {
            showOnboarding()
        } else if companionPreviewEnabled && !companionPreviewHidden {
            _ = companionPreviewController?.showIdle()
        }
    }

    // MARK: main window (settings & stats)

    private func installMainWindow(model: MoveBitModel) {
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 760, height: 840),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable],
                              backing: .buffered, defer: false)
        window.title = "MoveBit"
        window.minSize = NSSize(width: 460, height: 320)
        window.contentView = NSHostingView(rootView: MainView().environmentObject(model))
        window.center()
        window.isReleasedWhenClosed = false
        window.setFrameAutosaveName("MoveBitMainWindow")
        window.delegate = self
        mainWindow = window
    }

    func showMainWindow() {
        guard let window = mainWindow else { return }
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    // MARK: menu bar (tray)
    // The status item installs once with the droplet icon; the pause label
    // swaps with the paused state and labels follow the language, so refresh
    // the menu entries in place (no status-item flicker on rebuild).

    private func rebuildMenuBar() {
        guard let model else { return }
        var items: [RivetMenuItem] = [
            .action(label: L10n.t("TrayOpen"), identifier: "tray-open",
                    handler: { [weak self] in self?.showMainWindow() }),
            .action(label: model.paused.paused ? L10n.t("TrayResume") : L10n.t("TrayPause"),
                    identifier: "tray-pause",
                    handler: { [weak self] in self?.model?.togglePause() }),
            .action(label: L10n.t("TrayExit"), identifier: "tray-exit",
                    handler: { [weak self] in self?.quit() }),
        ]
        if companionPreviewEnabled {
            // Preview-only tray control: the non-interactive pet always has an
            // immediate local Hide action, even if the backend is unavailable.
            items.insert(.action(
                label: companionPreviewHidden ? "🐾 Show preview pet" : "🐾 Hide preview pet",
                identifier: "companion-preview-toggle",
                handler: { [weak self] in self?.toggleCompanionPreview() }),
                at: 2)
        }
        if let menuBar {
            menuBar.update(items: items)
        } else {
            let controller = RivetMenuBarController()
            controller.install(icon: MenuBarIcon.droplet, items: items)
            menuBar = controller
        }
    }

    private func toggleCompanionPreview() {
        guard companionPreviewEnabled else { return }
        companionPreviewHidden.toggle()
        if companionPreviewHidden {
            companionPreviewController?.hide()
        } else if model?.breakActive == nil {
            _ = companionPreviewController?.showIdle()
        }
        rebuildMenuBar()
    }

    func quit() {
        companionPreviewController?.hide()
        model?.flushNowSync()
        NSApp.terminate(nil)
    }

    // MARK: main menu
    // The app is accessory, so its menus show only while a window is focused.
    // This restores the standard keyboard plumbing (Cmd+Q, text commands for
    // the selectable update-failure text, Cmd+W/M on windows) that the SwiftUI
    // scene layer used to provide for free.

    private func installMenus() {
        let mainMenu = NSMenu()

        let appItem = NSMenuItem()
        mainMenu.addItem(appItem)
        let appMenu = NSMenu()
        appItem.submenu = appMenu
        appMenu.addItem(withTitle: L10n.t("MenuAbout"),
                        action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)),
                        keyEquivalent: "")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: L10n.t("TrayExit"),
                        action: #selector(NSApplication.terminate(_:)),
                        keyEquivalent: "q")

        let editItem = NSMenuItem()
        mainMenu.addItem(editItem)
        let editMenu = NSMenu(title: L10n.t("MenuEdit"))
        editItem.submenu = editMenu
        editMenu.addItem(withTitle: L10n.t("MenuUndo"),
                         action: Selector(("undo:")), keyEquivalent: "z")
        editMenu.addItem(withTitle: L10n.t("MenuRedo"),
                         action: Selector(("redo:")), keyEquivalent: "Z")
        editMenu.addItem(.separator())
        editMenu.addItem(withTitle: L10n.t("MenuCut"),
                         action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        editMenu.addItem(withTitle: L10n.t("MenuCopy"),
                         action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editMenu.addItem(withTitle: L10n.t("MenuPaste"),
                         action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editMenu.addItem(withTitle: L10n.t("MenuSelectAll"),
                         action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")

        let windowItem = NSMenuItem()
        mainMenu.addItem(windowItem)
        let windowMenu = NSMenu(title: L10n.t("MenuWindow"))
        windowItem.submenu = windowMenu
        windowMenu.addItem(withTitle: L10n.t("MenuClose"),
                           action: #selector(NSWindow.performClose(_:)),
                           keyEquivalent: "w")
        windowMenu.addItem(withTitle: L10n.t("MenuMinimize"),
                           action: #selector(NSWindow.performMiniaturize(_:)),
                           keyEquivalent: "m")

        NSApp.mainMenu = mainMenu
    }

    // MARK: reminders due

    private func handleRemindersDue(_ due: RemindersDue) {
        guard let model else { return }
        // The preview is a single mouse-through, edge-docked pet. The old
        // reminder path remains the fallback whenever preview is disabled,
        // hidden or cannot find a usable display.
        if companionPreviewEnabled && !companionPreviewHidden &&
            companionPreviewController?.show(due) == true {
            return
        }
        if due.kinds == [.micro] {
            // Micro-only tick: small top-center nudge, no lock, no sound.
            microController?.show(durationSeconds: model.config.micro_break_duration_seconds,
                                  title: CopyPool.pickMicro())
            return
        }

        // Merged toast: one card stands in for every kind due this tick.
        let lines = toastLines(due: due, model: model)
        let title: String
        if due.kinds.contains(.sit) {
            title = L10n.t("NotifSitTitle")
        } else if due.kinds.contains(.water) {
            title = L10n.t("NotifWaterTitle")
        } else {
            title = L10n.t("NotifSitTitle")
        }
        let accent: ReminderKind = due.kinds.contains(.sit) ? .sit
            : due.kinds.contains(.water) ? .water : .micro

        if model.config.sound_enabled {
            NSSound(named: "Ping")?.play()
        }
        toastController?.show(ToastContent(title: title, lines: lines, accent: accent,
                                           snoozeMinutes: model.config.snooze_minutes,
                                           kinds: due.kinds))
    }

    /// Per-kind line + a rotating pool line (parity with FlushTickReminders).
    private func toastLines(due: RemindersDue, model: MoveBitModel) -> [String] {
        var lines: [String] = []
        if due.kinds.contains(.sit) {
            lines.append(L10n.t("NotifSitBody",
                                CopyFormat.duration(model.today.active_mins),
                                "\(model.today.sit_breaks)"))
            lines.append(L10n.pick(CopyPool.sitToasts.0, CopyPool.sitToasts.1))
        }
        if due.kinds.contains(.water) {
            lines.append(L10n.t("NotifWaterCount", "\(model.today.water_reminders)"))
            lines.append(CopyPool.pickWater())
        }
        if due.kinds.contains(.micro) {
            lines.append(CopyPool.pickMicro())
        }
        return lines
    }

    /// Cheer toast when a completed break hits a milestone count.
    func showCheerToast(count: Int64) {
        guard let model else { return }
        showToast(title: L10n.t("NotifCheerTitle"),
                  lines: [L10n.t("NotifCheerBody", "\(count)",
                                  CopyFormat.duration(model.today.active_mins))],
                  accent: .sit)
    }

    func showToast(title: String, lines: [String], accent: ReminderKind) {
        guard let model else { return }
        toastController?.show(ToastContent(title: title, lines: lines, accent: accent,
                                           snoozeMinutes: model.config.snooze_minutes,
                                           kinds: []))
    }

    // MARK: forced break

    private func installOverlayControllers(model: MoveBitModel) {
        let overlays = BreakOverlayController()
        overlays.onSkip = { [weak self] in self?.model?.skipBreak() }
        overlays.onEmergencyExit = { [weak self] in self?.model?.emergencySkipBreak() }
        overlays.onComplete = { [weak self] in self?.model?.completeBreak() }
        overlays.hintProvider = { L10n.pick(CopyPool.breakHints.0, CopyPool.breakHints.1) }
        overlayController = overlays

        let toast = ToastPanelController()
        toast.onSnooze = { [weak self] kinds in self?.model?.snooze(kinds) }
        toastController = toast

        microController = MicroPanelController()
        if companionPreviewEnabled {
            companionPreviewController = CompanionPreviewController()
        }
    }

    private func handleBreakStarted() {
        guard let model, let state = model.breakActive else { return }
        companionPreviewController?.hide()
        // The full-screen break outranks transient nudges/toasts (parity).
        toastController?.dismiss()
        microController?.dismiss()
        if !overlayController!.show(state: state,
                                    snoozeMinutes: model.config.snooze_minutes) {
            // No screen to cover: at least say why (parity fallback).
            toastController?.show(ToastContent(
                title: L10n.t("NotifSitTitle"),
                lines: [L10n.t("NotifSitFallback")],
                accent: .sit,
                snoozeMinutes: model.config.snooze_minutes,
                kinds: []))
        }
    }

    private func handleBreakEnded(completed: Bool) {
        overlayController?.finish(completed: completed)
        if companionPreviewEnabled && !companionPreviewHidden {
            _ = companionPreviewController?.showIdle()
        }
        guard completed, let model else { return }
        let count = model.today.sit_breaks
        if CopyPool.celebrateAt.contains(count) {
            showCheerToast(count: count)
        }
    }

    // MARK: onboarding

    func showOnboarding() {
        if let window = onboardingWindow {
            window.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            return
        }
        guard let model else { return }
        let state = OnboardingState()
        onboardingState = state
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 500, height: 640),
                              styleMask: [.titled, .closable],
                              backing: .buffered, defer: false)
        window.title = L10n.t("ObTitle")
        window.contentView = NSHostingView(
            rootView: OnboardingView(state: state, model: model))
        window.center()
        window.isReleasedWhenClosed = false
        window.delegate = self
        onboardingWindow = window
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func closeOnboarding() {
        onboardingWindow?.orderOut(nil)
        onboardingWindow = nil
        onboardingState = nil
    }

    /// Called by the wizard's finish/skip: mark seen + settled toast (parity).
    func onboardingFinished() {
        if let state = onboardingState {
            state.finished = true
        }
        if companionPreviewEnabled && !companionPreviewHidden {
            _ = companionPreviewController?.showIdle()
        }
        showToast(title: L10n.t("ObSettled"),
                  lines: [L10n.t("ObSettledBody")],
                  accent: .water)
    }
}

extension AppDelegate: NSWindowDelegate {
    // Closing the main window hides it (the tray brings it back); Quit lives in
    // the tray menu only. The onboarding window really closes — closing midway
    // counts as seen and persists the current selections.
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        if sender === mainWindow {
            sender.orderOut(nil)
            return false
        }
        if sender === onboardingWindow {
            // Whatever was selected last is kept; onboarding cannot nag again.
            if let state = onboardingState, !state.finished, let model {
                state.persist(model: model, welcomeShown: true)
            }
            onboardingWindow = nil
            onboardingState = nil
            if companionPreviewEnabled && !companionPreviewHidden {
                _ = companionPreviewController?.showIdle()
            }
        }
        return true
    }
}
