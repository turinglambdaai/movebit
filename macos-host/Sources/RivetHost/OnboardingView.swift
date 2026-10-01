import SwiftUI
import AppKit

/// Shared wizard state owned by the AppDelegate so closing the window midway
/// (any path) persists the current selections and marks onboarding as seen.
@MainActor
final class OnboardingState: ObservableObject {
    @Published var page = 0
    @Published var mode = "standard"
    @Published var water: Int64 = 30
    @Published var sound = true
    var finished = false

    // Mode -> config mapping (parity with OnboardingWindow.ApplyMode).
    var sitMinutes: Int64 { mode == "gentle" ? 60 : mode == "strict" ? 30 : 45 }
    var forceEnabled: Bool { mode != "gentle" }
    var breakDuration: Int64 { mode == "gentle" ? 3 : 5 }

    /// One set-config call writes the whole pact.
    func persist(model: MoveBitModel, welcomeShown: Bool) {
        model.saveConfig(model.config.with(
            sit_reminder_minutes: sitMinutes,
            water_reminder_minutes: water,
            force_break_enabled: forceEnabled,
            break_duration_minutes: breakDuration,
            sound_enabled: sound,
            welcome_shown: welcomeShown))
    }
}

/// Four-step first-run wizard: meet the droplet -> strictness pact -> water +
/// sound -> contract summary. Kept deliberately small (plain page switching).
struct OnboardingView: View {
    @ObservedObject var state: OnboardingState
    @ObservedObject var model: MoveBitModel

    private let lastPage = 3

    var body: some View {
        VStack(spacing: 0) {
            Group {
                switch state.page {
                case 0: introPage
                case 1: strictPage
                case 2: waterPage
                default: pactPage
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
            controls
        }
        .padding(32)
        .background(Palette.appBg)
        .frame(width: 500, height: 640)
    }

    // MARK: pages

    private var introPage: some View {
        VStack(spacing: 14) {
            Text("💧")
                .font(.system(size: 72))
                .padding(.top, 24)
                .padding(.bottom, 8)
            Text(L10n.t("ObHello"))
                .font(.system(size: 26, weight: .bold))
                .foregroundColor(Palette.inkPrimary)
            Text(L10n.t("ObIntro"))
                .font(.system(size: 14.5))
                .foregroundColor(Palette.inkSecondary)
                .multilineTextAlignment(.center)
                .lineSpacing(4)
            Text(L10n.t("ObSteps"))
                .font(.system(size: 13))
                .foregroundColor(Palette.inkMuted)
                .padding(.top, 6)
        }
        .frame(maxWidth: .infinity)
    }

    private var strictPage: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(L10n.t("ObStrictTitle"))
                .font(.system(size: 22, weight: .bold))
                .foregroundColor(Palette.inkPrimary)
            Text(L10n.t("ObStrictQ"))
                .font(.system(size: 13))
                .foregroundColor(Palette.inkSecondary)
            modeCard(id: "gentle", title: L10n.t("ObModeGentle"), badge: nil,
                     desc: L10n.t("ObModeGentleDesc"))
            modeCard(id: "standard", title: L10n.t("ObModeStandard"),
                     badge: L10n.t("ObBadgeRecommended"),
                     desc: L10n.t("ObModeStandardDesc"))
            modeCard(id: "strict", title: L10n.t("ObModeStrict"),
                     badge: L10n.t("ObBadgeEvidence"),
                     desc: L10n.t("ObModeStrictDesc"))
            Text(L10n.t("ObStrictFootnote"))
                .font(.system(size: 11))
                .foregroundColor(Palette.inkMuted)
        }
    }

    private var waterPage: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(L10n.t("ObWaterTitle"))
                .font(.system(size: 20, weight: .bold))
                .foregroundColor(Palette.inkPrimary)
            HStack(spacing: 10) {
                waterCard(30)
                waterCard(45)
                waterCard(60)
            }
            Text(L10n.t("ObSoundTitle"))
                .font(.system(size: 15, weight: .semibold))
                .foregroundColor(Palette.inkPrimary)
                .padding(.top, 8)
            HStack {
                Toggle("", isOn: Binding(
                    get: { state.sound },
                    set: { state.sound = $0 }))
                    .labelsHidden()
                Text(L10n.t("ObSoundHint"))
                    .font(.system(size: 12))
                    .foregroundColor(Palette.inkSecondary)
            }
        }
    }

    private var pactPage: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(L10n.t("ObPactTitle"))
                .font(.system(size: 22, weight: .bold))
                .foregroundColor(Palette.inkPrimary)
            VStack(alignment: .leading, spacing: 8) {
                ForEach(summaryLines, id: \.self) { line in
                    Text(line)
                        .font(.system(size: 13.5))
                        .foregroundColor(Palette.inkSecondary)
                }
            }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: 12).fill(Palette.cardBg))
            .overlay(RoundedRectangle(cornerRadius: 12)
                .strokeBorder(Palette.cardBorder, lineWidth: 1))
            Text(L10n.t("ObPactOutro"))
                .font(.system(size: 13))
                .foregroundColor(Palette.inkMuted)
        }
        .padding(.top, 12)
    }

    private var summaryLines: [String] {
        let sit = state.forceEnabled
            ? L10n.t("ObSummaryForced", "\(state.sitMinutes)", "\(state.breakDuration)")
            : L10n.t("ObSummaryToast", "\(state.sitMinutes)")
        return [
            sit,
            L10n.t("ObSummaryWater", "\(state.water)"),
            L10n.t("ObSummaryAway", "\(model.config.away_reset_minutes)"),
            L10n.t("ObSummarySound", L10n.t(state.sound ? "ObOn" : "ObOff")),
        ]
    }

    // MARK: pieces

    private func modeCard(id: String, title: String, badge: String?, desc: String) -> some View {
        let selected = state.mode == id
        return VStack(alignment: .leading, spacing: 3) {
            HStack(spacing: 8) {
                Text(title)
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundColor(selected ? Palette.accent : Palette.inkPrimary)
                if let badge {
                    Text(badge)
                        .font(.system(size: 10.5, weight: .medium))
                        .foregroundColor(selected ? Palette.accent : Palette.inkMuted)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(RoundedRectangle(cornerRadius: 5).fill(Palette.accentSoft))
                }
            }
            Text(desc)
                .font(.system(size: 12.5))
                .foregroundColor(Palette.inkSecondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(14)
        .background(RoundedRectangle(cornerRadius: 12)
            .fill(selected ? Palette.accentSoft : Palette.cardBg))
        .overlay(RoundedRectangle(cornerRadius: 12)
            .strokeBorder(selected ? Palette.accent : Palette.cardBorder, lineWidth: 1))
        .contentShape(Rectangle())
        .onTapGesture { state.mode = id }
    }

    private func waterCard(_ minutes: Int64) -> some View {
        let selected = state.water == minutes
        return Text(L10n.t(minutes == 30 ? "ObMin30" : minutes == 45 ? "ObMin45" : "ObMin60"))
            .font(.system(size: 14, weight: selected ? .semibold : .regular))
            .foregroundColor(selected ? Palette.accent : Palette.inkPrimary)
            .frame(maxWidth: .infinity)
            .padding(.vertical, 14)
            .background(RoundedRectangle(cornerRadius: 12)
                .fill(selected ? Palette.accentSoft : Palette.cardBg))
            .overlay(RoundedRectangle(cornerRadius: 12)
                .strokeBorder(selected ? Palette.accent : Palette.cardBorder, lineWidth: 1))
            .contentShape(Rectangle())
            .onTapGesture { state.water = minutes }
    }

    // MARK: controls

    private var controls: some View {
        VStack(spacing: 14) {
            HStack(spacing: 8) {
                ForEach(0...lastPage, id: \.self) { index in
                    Circle()
                        .fill(index == state.page ? Palette.accent : Palette.trackBg)
                        .frame(width: 8, height: 8)
                }
            }
            HStack {
                if state.page < lastPage {
                    Button(L10n.t("ObSkip")) { skipToDefaults() }
                        .buttonStyle(.plain)
                        .foregroundColor(Palette.inkMuted)
                }
                Spacer()
                Button(nextLabel) { next() }
                    .buttonStyle(.borderedProminent)
                    .tint(Palette.accent)
            }
        }
        .padding(.top, 12)
    }

    private var nextLabel: String {
        state.page == 0 ? L10n.t("ObStart")
            : state.page == lastPage ? L10n.t("ObLaunch")
            : L10n.t("ObNext")
    }

    private func next() {
        if state.page >= lastPage {
            finish()
        } else {
            state.page += 1
        }
    }

    /// Skip = plain defaults, as if the user never touched anything.
    private func skipToDefaults() {
        state.mode = "standard"
        state.water = 30
        state.sound = true
        finish()
    }

    private func finish() {
        state.finished = true
        state.persist(model: model, welcomeShown: true)
        AppDelegate.shared?.onboardingFinished()
        AppDelegate.shared?.closeOnboarding()
    }
}
