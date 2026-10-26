import AppKit
import SwiftUI

// Deliberately experimental: enabled only by MOVEBIT_COMPANION_PREVIEW=1.
// This is a mouse-through visual feasibility slice, NOT the final pet UX.
// No screenshot capture, accessibility permissions, extra scheduler or RPC.
//
// The panel cannot steal keyboard/pointer input: the *whole window* ignores
// mouse events. Interactions and persisted preferences come in later slices,
// once native hit-test feasibility is proven for every supported host.

private enum CompanionPreviewGesture {
    case idle
    case water
    case micro
    case sit

    var symbol: String {
        switch self {
        case .idle:  return "sparkle"
        case .water: return "drop.fill"
        case .micro: return "eye"
        case .sit:   return "figure.stand"
        }
    }

    var accessibilityTitle: String {
        switch self {
        case .idle:  return "MoveBit"
        case .water: return L10n.t("NotifWaterTitle")
        case .micro: return L10n.t("MicroHint")
        case .sit:   return L10n.t("NotifSitTitle")
        }
    }
}

@MainActor
final class CompanionPreviewController: NSObject {
    private var panel: NSPanel?
    private var generation = 0

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

    @discardableResult
    func showIdle() -> Bool {
        present(.idle)
    }

    /// Returns false if no screen is available; caller uses safe fallback.
    func show(_ due: RemindersDue) -> Bool {
        let gesture: CompanionPreviewGesture
        if due.kinds.contains(.sit) {
            gesture = .sit
        } else if due.kinds.contains(.water) {
            gesture = .water
        } else {
            gesture = .micro
        }
        guard present(gesture) else { return false }

        // Return to calm idle instead of repeating or escalating reminders.
        // Newer cues supersede older expiry tasks; the backend still owns all
        // snooze, history, timing and reminder state.
        let currentGeneration = generation
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 7_000_000_000)
            guard let self, self.generation == currentGeneration else { return }
            _ = self.present(.idle)
        }
        return true
    }

    func hide() {
        generation += 1
        panel?.orderOut(nil)
    }

    private func present(_ gesture: CompanionPreviewGesture) -> Bool {
        guard let screen = NSScreen.main ?? NSScreen.screens.first else {
            hide()
            return false
        }
        generation += 1
        let panel = self.panel ?? makePanel()
        self.panel = panel
        panel.contentView = NSHostingView(
            rootView: CompanionPreviewPetView(gesture: gesture))
        position(panel, on: screen)
        // NSPanel never becomes key or main, and ignoresMouseEvents is true.
        panel.orderFrontRegardless()
        return true
    }

    private func makePanel() -> NSPanel {
        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 176, height: 130),
            styleMask: [.nonactivatingPanel, .borderless],
            backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.level = .floating
        panel.ignoresMouseEvents = true
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        // Do not join all Spaces / full-screen Spaces in this first preview:
        // presentation safety is more important than the pet being omnipresent.
        panel.collectionBehavior = [.transient]
        panel.isMovableByWindowBackground = false
        return panel
    }

    private func position(_ panel: NSPanel, on screen: NSScreen) {
        // Fixed, bounded edge docking; never travel across the work surface.
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

/// An original, vector-drawn mascot: no borrowed Codex assets or images.
private struct CompanionPreviewPetView: View {
    let gesture: CompanionPreviewGesture

    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var perk = false

    var body: some View {
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

            if gesture != .idle {
                Text(gesture.accessibilityTitle)
                    .font(.system(size: 11, weight: .semibold))
                    .lineLimit(1)
                    .foregroundColor(Color(red: 0.18, green: 0.23, blue: 0.30))
                    .padding(.horizontal, 10)
                    .padding(.vertical, 5)
                    .background(Capsule().fill(Color.white.opacity(0.97)))
                    .offset(y: 55)

                Image(systemName: gesture.symbol)
                    .font(.system(size: 14, weight: .bold))
                    .foregroundColor(Color(red: 0.28, green: 0.52, blue: 0.72))
                    .padding(8)
                    .background(Circle().fill(Color.white))
                    .offset(x: 30, y: -28)
                    .accessibilityLabel(gesture.accessibilityTitle)
            }
        }
        .frame(width: 176, height: 130)
        .offset(y: perk ? -5 : 0)
        .onAppear {
            guard gesture != .idle, !reduceMotion else { return }
            // At most two short hops on a new cue; no infinite attention loop.
            withAnimation(.easeInOut(duration: 0.28)
                .repeatCount(2, autoreverses: true)) {
                perk = true
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(gesture.accessibilityTitle)
    }
}
