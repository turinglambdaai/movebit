import SwiftUI
import AppKit

/// Shared wizard state owned by the AppDelegate so closing the window midway
/// (any path) persists the current selections and marks onboarding as seen.
@MainActor
final class OnboardingState: ObservableObject {
    @Published var page = 0
    @Published var water: Int64 = 30
    @Published var sit: Int64 = 45
    @Published var sound = true
    var finished = false

    /// One set-config call writes the whole first-run choice. There is no
    /// strictness pact in v1: one gentle, non-blocking reminder experience.
    func persist(model: MoveBitModel, welcomeShown: Bool) {
        model.saveConfig(model.config.with(
            sit_reminder_minutes: sit,
            water_reminder_minutes: water,
            sound_enabled: sound,
            welcome_shown: welcomeShown))
    }
}

/// Three-step first-run flow: meet MoveBit -> reminders (water cadence +
/// sound) -> contract summary. Kept deliberately small (plain page switching).
struct OnboardingView: View {
    @ObservedObject var state: OnboardingState
    @ObservedObject var model: MoveBitModel

    private let lastPage = 2

    var body: some View {
        VStack(spacing: 0) {
            Group {
                switch state.page {
                case 0: introPage
                case 1: remindersPage
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

    private var remindersPage: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(L10n.t("ObWaterTitle"))
                .font(.system(size: 20, weight: .bold))
                .foregroundColor(Palette.inkPrimary)
            HStack(spacing: 10) {
                waterCard(30, label: "ObMin30")
                waterCard(45, label: "ObMin45")
                waterCard(60, label: "ObMin60")
            }
            HStack(spacing: 10) {
                sitCard(45, label: "ObSit45")
                sitCard(60, label: "ObSit60")
                sitCard(90, label: "ObSit90")
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
        [
            L10n.t("ObSummaryToast", "\(state.sit)"),
            L10n.t("ObSummaryWater", "\(state.water)"),
            L10n.t("ObSummaryAway", "\(model.config.away_reset_minutes)"),
            L10n.t("ObSummarySound", L10n.t(state.sound ? "ObOn" : "ObOff")),
        ]
    }

    // MARK: pieces

    private func waterCard(_ minutes: Int64, label: String) -> some View {
        choiceCard(selected: state.water == minutes, label: L10n.t(label)) {
            state.water = minutes
        }
    }

    private func sitCard(_ minutes: Int64, label: String) -> some View {
        choiceCard(selected: state.sit == minutes, label: L10n.t(label)) {
            state.sit = minutes
        }
    }

    private func choiceCard(selected: Bool, label: String, pick: @escaping () -> Void) -> some View {
        Text(label)
            .font(.system(size: 14, weight: selected ? .semibold : .regular))
            .foregroundColor(selected ? Palette.accent : Palette.inkPrimary)
            .frame(maxWidth: .infinity)
            .padding(.vertical, 14)
            .background(RoundedRectangle(cornerRadius: 12)
                .fill(selected ? Palette.accentSoft : Palette.cardBg))
            .overlay(RoundedRectangle(cornerRadius: 12)
                .strokeBorder(selected ? Palette.accent : Palette.cardBorder, lineWidth: 1))
            .contentShape(Rectangle())
            .onTapGesture(perform: pick)
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

    /// Skip = the shipped defaults, as if the user opted out of choices.
    private func skipToDefaults() {
        state.water = 30
        state.sit = 45
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
