import SwiftUI
import AppKit

// The MoveBit companion: a small original character near the screen edge that
// DELIVERS the backend's health cues (issue #51 — reminders first, the
// character is presentation, never a pet product).
//
// Safety contract (issue #54): the panel is fully pointer-through —
// `ignoresMouseEvents` on the whole window — because native partial hit
// regions were not proven safe. The reminder's Done / Later / Hide controls
// are therefore mandatory tray items (MainWindow owns them), which the same
// contract explicitly allows. The panel never becomes key or main, never
// grabs focus and never moves across the work surface: one bounded edge
// dock, re-anchored when displays change.
//
// Every gesture carries the actionable health text (a speech bubble next to
// the mascot and an accessibility label on the whole element), so the
// reminder is understandable without recognizing an animation; Reduce Motion
// or the in-app motion toggle renders everything static.

enum CompanionGesture {
    case idle
    case stretch   // sit: stand up / stretch
    case water     // drink water
    case blink     // micro: rest your eyes

    /// Semantic suggested action -> visual rendering hint.
    static func from(action: CueAction) -> CompanionGesture {
        switch action {
        case .stand_and_stretch: return .stretch
        case .drink_water: return .water
        case .rest_eyes: return .blink
        @unknown default: return .idle
        }
    }

    var symbol: String {
        switch self {
        case .idle: return "sparkle"
        case .water: return "drop.fill"
        case .blink: return "eye"
        case .stretch: return "figure.stand"
        }
    }
}

@MainActor
final class CompanionController: NSObject {
    private var panel: NSPanel?
    private var generation = 0
    private var idleTask: Task<Void, Never>?

    override init() {
        super.init()
        NotificationCenter.default.addObserver(
            self, selector: #selector(screenParametersChanged(_:)),
            name: NSApplication.didChangeScreenParametersNotification,
            object: nil)
    }

    deinit {
        NotificationCenter.default.removeObserver(self)
    }

    /// Show a due cue: gesture + actionable text. `animated` comes from the
    /// backend's plan (Reduce Motion / the motion preference both force a
    /// static cue). The bubble rests for a few seconds, then the character
    /// returns to its calm idle pose — the cue itself stays open in the
    /// backend ledger until the user acts (tray) or the expiry policy closes
    /// it; no animation event ever touches reminder state.
    func show(gesture: CompanionGesture, message: String, animated: Bool) {
        present(gesture, message: message, animated: animated)
        idleTask?.cancel()
        idleTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 8_000_000_000)
            guard let self, !Task.isCancelled else { return }
            self.present(.idle, message: "", animated: false)
        }
    }

    /// Calm presence between cues (launch, after the last cue settles).
    func showIdle() {
        idleTask?.cancel()
        present(.idle, message: "", animated: false)
    }

    func hide() {
        idleTask?.cancel()
        generation += 1
        panel?.orderOut(nil)
    }

    private func present(_ gesture: CompanionGesture,
                         message: String, animated: Bool) {
        guard let screen = NSScreen.main ?? NSScreen.screens.first else {
            hide()
            return
        }
        generation += 1
        let panel = self.panel ?? makePanel()
        self.panel = panel
        panel.contentView = NSHostingView(
            rootView: CompanionView(gesture: gesture, message: message,
                                    animated: animated))
        position(panel, on: screen)
        // NSPanel never becomes key or main; the whole window ignores mouse
        // events (the #54 pointer-through fallback).
        panel.orderFrontRegardless()
    }

    private func makePanel() -> NSPanel {
        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 200, height: 150),
            styleMask: [.nonactivatingPanel, .borderless],
            backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.level = .floating
        panel.ignoresMouseEvents = true
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        // Deliberately transient: presentation safety outranks the character
        // being omnipresent — it does not follow into full-screen Spaces.
        panel.collectionBehavior = [.transient]
        panel.isMovableByWindowBackground = false
        return panel
    }

    /// Fixed, bounded edge docking; the character never travels across the
    /// work surface.
    private func position(_ panel: NSPanel, on screen: NSScreen) {
        let safe = screen.visibleFrame
        let size = panel.frame.size
        panel.setFrameOrigin(NSPoint(x: safe.maxX - size.width - 20,
                                     y: safe.minY + 20))
    }

    @objc private func screenParametersChanged(_ note: Notification) {
        guard let panel, panel.isVisible else { return }
        guard let screen = NSScreen.main ?? NSScreen.screens.first else {
            hide()
            return
        }
        position(panel, on: screen)
    }
}

/// The original, vector-drawn mascot (no borrowed assets) with an actionable
/// speech bubble. One accessibility element carries the reminder text, so
/// VoiceOver users get the health advice without seeing the animation.
private struct CompanionView: View {
    let gesture: CompanionGesture
    let message: String
    let animated: Bool

    @Environment(\.accessibilityReduceMotion) private var systemReduceMotion
    @State private var perk = false

    private var motionAllowed: Bool { animated && !systemReduceMotion }

    var body: some View {
        ZStack(alignment: .bottom) {
            if !message.isEmpty {
                Text(message)
                    .font(.system(size: 12, weight: .semibold))
                    .lineLimit(2)
                    .multilineTextAlignment(.center)
                    .foregroundColor(Color(red: 0.18, green: 0.23, blue: 0.30))
                    .padding(.horizontal, 12)
                    .padding(.vertical, 6)
                    .background(
                        RoundedRectangle(cornerRadius: 12)
                            .fill(Color.white.opacity(0.97)))
                    .overlay(alignment: .bottom) {
                        TriangleTail()
                            .fill(Color.white.opacity(0.97))
                            .frame(width: 12, height: 6)
                            .offset(y: 6)
                    }
                    .padding(.bottom, 74)
                    .transition(.opacity)
            }

            ZStack {
                // Two ears.
                Ellipse()
                    .fill(Color(red: 0.82, green: 0.60, blue: 0.46))
                    .frame(width: 18, height: 33)
                    .rotationEffect(.degrees(-23))
                    .offset(x: -18, y: -27)
                Ellipse()
                    .fill(Color(red: 0.82, green: 0.60, blue: 0.46))
                    .frame(width: 18, height: 33)
                    .rotationEffect(.degrees(23))
                    .offset(x: 18, y: -27)

                // Body/face.
                Ellipse()
                    .fill(Color(red: 0.98, green: 0.83, blue: 0.64))
                    .frame(width: 72, height: 68)
                    .overlay {
                        HStack(spacing: 21) {
                            Circle().fill(Color(red: 0.22, green: 0.18, blue: 0.17))
                                .frame(width: 6, height: 7)
                            Circle().fill(Color(red: 0.22, green: 0.18, blue: 0.17))
                                .frame(width: 6, height: 7)
                        }
                        .offset(y: -8)
                    }
                    .overlay {
                        Image(systemName: "heart.fill")
                            .font(.system(size: 8))
                            .foregroundColor(Color(red: 0.71, green: 0.36, blue: 0.37))
                            .offset(y: 10)
                    }
                    .offset(y: 9)

                // The gesture badge: WHAT to do, always readable.
                if gesture != .idle {
                    Image(systemName: gesture.symbol)
                        .font(.system(size: 14, weight: .bold))
                        .foregroundColor(Color(red: 0.28, green: 0.52, blue: 0.72))
                        .padding(8)
                        .background(Circle().fill(Color.white))
                        .offset(x: 34, y: -30)
                }
            }
            .frame(width: 176, height: 130)
            .offset(y: perk && motionAllowed ? -5 : 0)
        }
        .frame(width: 200, height: 150)
        .onAppear {
            guard gesture != .idle, motionAllowed else { return }
            // At most two short hops on a new cue; no infinite attention loop.
            withAnimation(.easeInOut(duration: 0.28)
                .repeatCount(2, autoreverses: true)) {
                perk = true
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(message.isEmpty
                            ? "MoveBit"
                            : message)
    }
}

private struct TriangleTail: Shape {
    func path(in rect: CGRect) -> Path {
        var path = Path()
        path.move(to: CGPoint(x: rect.minX, y: rect.minY))
        path.addLine(to: CGPoint(x: rect.maxX, y: rect.minY))
        path.addLine(to: CGPoint(x: rect.midX, y: rect.maxY))
        path.closeSubpath()
        return path
    }
}
