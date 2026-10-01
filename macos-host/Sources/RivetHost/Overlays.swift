import SwiftUI
import AppKit

// MARK: - forced break overlay
// One fullscreen window per screen at screenSaver level; the only user-facing
// control is the delayed Skip button. No Esc/keyboard cancel (the overlay is
// the point).

@MainActor
final class BreakOverlayController: ObservableObject {
    @Published var remainingSec = 0
    @Published var totalSec = 0
    @Published var skipAvailable = false
    @Published var goodbye = false
    @Published var hint = ""

    var onSkip: (() -> Void)?
    var onComplete: (() -> Void)?
    var hintProvider: (() -> String)?

    private var windows: [NSWindow] = []
    private var state: BreakState?
    private var running = false
    private var completedNudged = false

    /// Builds the overlay windows. Returns false when no screen is available
    /// (the caller falls back to a toast, parity with the old StartForcedBreak).
    @discardableResult
    func show(state: BreakState, snoozeMinutes: Int64) -> Bool {
        let screens = NSScreen.screens
        guard !screens.isEmpty else { return false }

        closeAll()
        self.state = state
        totalSec = Int(max(0, state.ends_at_ms - state.started_at_ms) / 1000)
        remainingSec = totalSec
        skipAvailable = false
        goodbye = false
        completedNudged = false
        hint = hintProvider?() ?? ""
        running = true
        tickLoop()

        for screen in screens {
            let window = NSWindow(contentRect: screen.frame,
                                  styleMask: [.borderless],
                                  backing: .buffered, defer: false)
            window.level = .screenSaver
            window.isOpaque = true
            window.hasShadow = false
            window.backgroundColor = NSColor(hex: "#0D1220")
            window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
            window.isReleasedWhenClosed = false
            window.ignoresMouseEvents = false
            window.animationBehavior = .none
            let hosting = NSHostingView(
                rootView: BreakOverlayContent(controller: self, snoozeMinutes: snoozeMinutes))
            hosting.frame = NSRect(origin: .zero, size: screen.frame.size)
            hosting.autoresizingMask = [.width, .height]
            window.contentView = hosting
            window.setFrame(screen.frame, display: true)
            window.orderFrontRegardless()
            windows.append(window)
        }
        return true
    }

    /// Break ended: completed runs the droplet goodbye, skip just closes.
    func finish(completed: Bool) {
        running = false
        if completed {
            goodbye = true
            Task { [weak self] in
                try? await Task.sleep(nanoseconds: 950_000_000)
                self?.closeAll()
            }
        } else {
            closeAll()
        }
    }

    func closeAll() {
        for window in windows {
            window.orderOut(nil)
        }
        windows.removeAll()
        state = nil
        running = false
    }

    /// One-second loop off the backend clock (ends-at-ms is authoritative).
    private func tickLoop() {
        Task { [weak self] in
            while let self = self, self.running {
                try? await Task.sleep(nanoseconds: 1_000_000_000)
                guard let state = self.state, self.running else { break }
                self.tick(state: state)
            }
        }
    }

    private func tick(state: BreakState) {
        let now = Int64(Date().timeIntervalSince1970 * 1000)
        remainingSec = Int(max(0, (state.ends_at_ms - now) / 1000))
        skipAvailable = now >= state.started_at_ms + state.skip_after_ms

        let elapsed = Int(max(0, now - state.started_at_ms) / 1000)
        if elapsed > 0, elapsed % 25 == 0, elapsed != totalSec {
            hint = hintProvider?() ?? hint
        }

        if remainingSec <= 0 {
            // Nudge the scheduler once; it stays authoritative for break-ended.
            if !completedNudged {
                completedNudged = true
                onComplete?()
            }
            // Watchdog: if the event never arrives (backend tick gap), close.
            if now > state.ends_at_ms + 5000 {
                closeAll()
            }
        }
    }
}

private struct BreakOverlayContent: View {
    @ObservedObject var controller: BreakOverlayController
    let snoozeMinutes: Int64
    @State private var haloPulse = false

    var body: some View {
        ZStack {
            LinearGradient(colors: [
                Color(hex: "#141B2E").opacity(0.95),
                Color(hex: "#1B1233").opacity(0.95),
                Color(hex: "#0D1220").opacity(0.95),
            ], startPoint: .topLeading, endPoint: .bottomTrailing)
            .ignoresSafeArea()

            VStack(spacing: 22) {
                Text(controller.goodbye ? "💪" : "💧")
                    .font(.system(size: controller.goodbye ? 56 : 64))
                    .opacity(controller.goodbye ? 0 : 1)
                    .offset(y: controller.goodbye ? -60 : 0)
                    .animation(.easeIn(duration: 0.6), value: controller.goodbye)
                Text(controller.goodbye ? L10n.t("BrkDone") : L10n.t("BrkTitle"))
                    .font(.system(size: 30, weight: .semibold))
                    .foregroundColor(Color(hex: "#F0EEE6"))
                countdownRing
                Text(controller.goodbye ? L10n.t("BrkGoodbye") : controller.hint)
                    .font(.system(size: 18))
                    .foregroundColor(Color(hex: "#B8B2C7"))
                    .multilineTextAlignment(.center)
                Text(L10n.t("BrkAutoUnlock"))
                    .font(.system(size: 12.5))
                    .foregroundColor(Color(hex: "#6E6884"))
            }

            if controller.skipAvailable && !controller.goodbye {
                VStack {
                    Spacer()
                    Button(L10n.t("BrkSkip", "\(snoozeMinutes)")) { controller.onSkip?() }
                        .buttonStyle(.plain)
                        .font(.system(size: 13.5))
                        .foregroundColor(Color(hex: "#8A849E"))
                        .padding(.horizontal, 16)
                        .padding(.vertical, 7)
                        .background(
                            RoundedRectangle(cornerRadius: 8)
                                .strokeBorder(Color(hex: "#3A3550"), lineWidth: 1))
                        .padding(.bottom, 48)
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onAppear { haloPulse = false }
    }

    private var countdownRing: some View {
        let fraction = controller.totalSec > 0
            ? Double(controller.remainingSec) / Double(controller.totalSec) : 0
        let sand = Color(hex: "#E8B77A")
        return ZStack {
            Circle()
                .stroke(sand.opacity(haloPulse ? 1 : 0.35), lineWidth: 2)
                .frame(width: 300, height: 300)
                .animation(.easeInOut(duration: 2).repeatForever(autoreverses: true),
                           value: haloPulse)
            Circle()
                .stroke(Color(hex: "#2A2840"), lineWidth: 10)
                .frame(width: 270, height: 270)
            Circle()
                .trim(from: 0, to: CGFloat(fraction))
                .stroke(sand, style: StrokeStyle(lineWidth: 10, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .frame(width: 270, height: 270)
                .opacity(controller.goodbye ? 0.35 : 1)
            VStack(spacing: 0) {
                if controller.goodbye {
                    Text("💪")
                        .font(.system(size: 56, weight: .bold))
                } else {
                    Text(timeString(controller.remainingSec))
                        .font(.system(size: 76, weight: .bold, design: .monospaced))
                        .foregroundColor(.white)
                }
            }
            Text(L10n.t("BrkUntilUnlock"))
                .font(.system(size: 13))
                .foregroundColor(Color(hex: "#8A84A0"))
                .frame(maxHeight: .infinity, alignment: .bottom)
                .padding(.bottom, 58)
                .opacity(controller.goodbye ? 0 : 1)
        }
        .frame(width: 300, height: 300)
        .onAppear { haloPulse = true }
    }

    private func timeString(_ seconds: Int) -> String {
        String(format: "%d:%02d", seconds / 60, seconds % 60)
    }
}

// MARK: - reminder toast
// Non-activating bottom-right panel, 9 s auto-close paused on hover; one card
// lists all due kinds. Snoozing a merged toast postpones every merged kind.

struct ToastContent {
    let title: String
    let lines: [String]
    let accent: ReminderKind
    let snoozeMinutes: Int64
    let kinds: [ReminderKind]
}

@MainActor
final class ToastPanelController: ObservableObject {
    var onSnooze: (([ReminderKind]) -> Void)?

    private var panel: NSPanel?
    private var dismissGeneration = 0

    func show(_ content: ToastContent) {
        let panel = self.panel ?? makePanel()
        self.panel = panel
        let hosting = NSHostingView(rootView: ToastView(content: content, controller: self))
        hosting.sizingOptions = .preferredContentSize
        panel.contentView = hosting
        hosting.layoutSubtreeIfNeeded()
        panel.setContentSize(hosting.fittingSize)
        position(panel)
        panel.orderFrontRegardless()
        restartAutoClose()
    }

    func restartAutoClose() {
        // Called on pointer enter and exit: hovering holds the card open.
        dismissGeneration += 1
        let generation = dismissGeneration
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 9_000_000_000)
            guard let self, generation == self.dismissGeneration else { return }
            self.dismiss()
        }
    }

    func snoozeTapped(_ kinds: [ReminderKind]) {
        onSnooze?(kinds)
        dismiss()
    }

    func dismiss() {
        dismissGeneration += 1
        panel?.orderOut(nil)
    }

    private func makePanel() -> NSPanel {
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 380, height: 120),
                            styleMask: [.nonactivatingPanel, .borderless],
                            backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.level = .floating
        panel.becomesKeyOnlyIfNeeded = true
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        return panel
    }

    private func position(_ panel: NSPanel) {
        guard let visible = NSScreen.main?.visibleFrame else { return }
        let size = panel.frame.size
        let origin = NSPoint(x: visible.maxX - size.width - 16,
                             y: visible.minY + 16)
        panel.setFrameOrigin(origin)
    }
}

private struct ToastView: View {
    let content: ToastContent
    @ObservedObject var controller: ToastPanelController

    var body: some View {
        HStack(spacing: 0) {
            Rectangle()
                .fill(Palette.kindAccent(content.accent))
                .frame(width: 5)
            VStack(alignment: .leading, spacing: 10) {
                Text(content.title)
                    .font(.system(size: 17, weight: .semibold))
                    .foregroundColor(Color(hex: "#26241F"))
                VStack(alignment: .leading, spacing: 3) {
                    ForEach(content.lines.indices, id: \.self) { index in
                        Text(content.lines[index])
                            .font(.system(size: 13.5))
                            .opacity(0.75)
                            .foregroundColor(Color(hex: "#6E6A5E"))
                    }
                }
                HStack(spacing: 10) {
                    Spacer()
                    if !content.kinds.isEmpty {
                        Button(L10n.t("NotifSnooze", "\(content.snoozeMinutes)")) {
                            controller.snoozeTapped(content.kinds)
                        }
                        .buttonStyle(.plain)
                        .font(.system(size: 12.5))
                        .foregroundColor(Color(hex: "#6E6A5E"))
                        .padding(.horizontal, 10)
                        .padding(.vertical, 5)
                        .background(RoundedRectangle(cornerRadius: 7)
                            .strokeBorder(Color(hex: "#E2DCCC"), lineWidth: 1))
                    }
                    Button(L10n.t("NotifGotIt")) { controller.dismiss() }
                        .buttonStyle(.plain)
                        .font(.system(size: 12.5))
                        .foregroundColor(Color(hex: "#26241F"))
                        .padding(.horizontal, 10)
                        .padding(.vertical, 5)
                        .background(RoundedRectangle(cornerRadius: 7)
                            .fill(Color(hex: "#F3EEE3")))
                }
            }
            .padding(EdgeInsets(top: 16, leading: 20, bottom: 14, trailing: 20))
        }
        .frame(width: 380)
        .background(RoundedRectangle(cornerRadius: 14).fill(Color(hex: "#FFFDFB")))
        .clipShape(RoundedRectangle(cornerRadius: 14))
        .onHover { _ in controller.restartAutoClose() }
    }
}

// MARK: - micro break
// Small top-center card with a thin shrinking progress bar. Click / Esc /
// timeout dismisses; never locks, never steals focus, silent by design.

@MainActor
final class MicroPanelController: ObservableObject {
    @Published var progress = 1.0

    private var panel: NSPanel?
    private var generation = 0
    private var keyMonitor: Any?

    func show(durationSeconds: Int64, title: String) {
        dismiss()
        generation += 1
        let generation = generation
        progress = 1

        let panel = self.panel ?? makePanel()
        self.panel = panel
        let hosting = NSHostingView(rootView: MicroBreakView(title: title, controller: self))
        hosting.sizingOptions = .preferredContentSize
        panel.contentView = hosting
        hosting.layoutSubtreeIfNeeded()
        panel.setContentSize(hosting.fittingSize)
        position(panel)
        panel.orderFrontRegardless()
        installKeyMonitor()

        let totalNanos = UInt64(max(1, durationSeconds) * 1_000_000_000)
        let tick = min(totalNanos, 250_000_000)
        Task { [weak self] in
            while let self, generation == self.generation {
                try? await Task.sleep(nanoseconds: tick)
                guard generation == self.generation else { return }
                self.progress = max(0, self.progress - Double(tick) / Double(totalNanos))
                if self.progress <= 0 {
                    self.dismiss()
                }
            }
        }
    }

    func dismiss() {
        generation += 1
        panel?.orderOut(nil)
        if let keyMonitor {
            NSEvent.removeMonitor(keyMonitor)
            self.keyMonitor = nil
        }
    }

    private func installKeyMonitor() {
        guard keyMonitor == nil else { return }
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if event.keyCode == 53 { // Escape
                if let self {
                    MainActor.assumeIsolated { self.dismiss() }
                }
                return nil
            }
            return event
        }
    }

    private func makePanel() -> NSPanel {
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 400, height: 110),
                            styleMask: [.nonactivatingPanel, .borderless],
                            backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.level = .floating
        panel.becomesKeyOnlyIfNeeded = true
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        return panel
    }

    /// Upper third of the primary screen, in the sight line (parity).
    private func position(_ panel: NSPanel) {
        guard let visible = NSScreen.main?.visibleFrame else { return }
        let size = panel.frame.size
        let origin = NSPoint(x: visible.midX - size.width / 2,
                             y: visible.maxY - visible.height * 0.28 - size.height)
        panel.setFrameOrigin(origin)
    }
}

private struct MicroBreakView: View {
    let title: String
    @ObservedObject var controller: MicroPanelController

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack(spacing: 10) {
                Text("👀").font(.system(size: 22))
                Text(title)
                    .font(.system(size: 18, weight: .semibold))
                    .foregroundColor(Palette.inkPrimary)
            }
            Text(L10n.t("MicroHint"))
                .font(.system(size: 11.5))
                .foregroundColor(Palette.inkMuted)
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    RoundedRectangle(cornerRadius: 2).fill(Palette.trackBg)
                    RoundedRectangle(cornerRadius: 2)
                        .fill(Palette.accent)
                        .frame(width: geo.size.width * CGFloat(controller.progress))
                }
            }
            .frame(height: 4)
            .padding(.top, 6)
        }
        .padding(EdgeInsets(top: 18, leading: 26, bottom: 18, trailing: 26))
        .frame(width: 400)
        .background(RoundedRectangle(cornerRadius: 18).fill(Palette.cardBg))
        .overlay(RoundedRectangle(cornerRadius: 18)
            .strokeBorder(Palette.cardBorder, lineWidth: 1))
        .clipShape(RoundedRectangle(cornerRadius: 18))
        .contentShape(Rectangle())
        .onTapGesture { controller.dismiss() }
    }
}
