import SwiftUI
import AppKit
import RivetEmbedding
import RivetRuntime
import RivetSystem

@main
struct MoveBitApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    // Tray app: all windows are AppKit windows owned by the delegate.
    var body: some Scene {
        Settings { EmptyView() }
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
        NSApp.setActivationPolicy(.accessory)

        let model = MoveBitModel()
        self.model = model

        model.onReady = { [weak self] in self?.handleReady() }
        model.onConfigChanged = { [weak self] in self?.rebuildMenuBar() }
        model.onRemindersDue = { [weak self] due in self?.handleRemindersDue(due) }
        model.onBreakStarted = { [weak self] in self?.handleBreakStarted() }
        model.onBreakEnded = { [weak self] completed in self?.handleBreakEnded(completed: completed) }

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
        if !model.config.welcome_shown {
            showOnboarding()
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
    // RivetMenuBarController's items are fixed at install time and the pause
    // label swaps with the paused state, so rebuild on state/language changes.

    private func rebuildMenuBar() {
        guard let model else { return }
        menuBar?.remove()
        let controller = RivetMenuBarController()
        controller.install(title: "MoveBit", menuItems: [
            (L10n.t("TrayOpen"), "tray-open", { [weak self] in self?.showMainWindow() }),
            (model.paused.paused ? L10n.t("TrayResume") : L10n.t("TrayPause"),
             "tray-pause", { [weak self] in self?.model?.togglePause() }),
            (L10n.t("TrayExit"), "tray-exit", { [weak self] in self?.quit() }),
        ])
        menuBar = controller
    }

    func quit() {
        model?.flushNowSync()
        NSApp.terminate(nil)
    }

    // MARK: reminders due

    private func handleRemindersDue(_ due: RemindersDue) {
        guard let model else { return }
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
        overlays.onComplete = { [weak self] in self?.model?.completeBreak() }
        overlays.hintProvider = { L10n.pick(CopyPool.breakHints.0, CopyPool.breakHints.1) }
        overlayController = overlays

        let toast = ToastPanelController()
        toast.onSnooze = { [weak self] kinds in self?.model?.snooze(kinds) }
        toastController = toast

        microController = MicroPanelController()
    }

    private func handleBreakStarted() {
        guard let model, let state = model.breakActive else { return }
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
        }
        return true
    }
}
