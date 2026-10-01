import SwiftUI
import RivetEmbedding
import RivetRuntime
import RivetSystem

/// Copy-pool identifiers emitted by gen-swift-strings.mjs (indexed keys).
enum CopyPool {
    static let breakHints = ("CopyBreakHintPool", 10)
    static let sitToasts = ("CopySitToastPool", 6)
    static let water = ("CopyWaterPool", 8)
    static let micro = ("CopyMicroPool", 9)
    static let lateNightWater = ("CopyLateNightWaterPool", 3)
    static let lateNightMicro = ("CopyLateNightMicroPool", 3)

    /// After 22:00 the droplet drops the jokes (parity with BreakCopy.IsLateNight,
    /// 22:00-06:00 per copy.lateNightWindow).
    static var isLateNight: Bool {
        let hour = Calendar.current.component(.hour, from: Date())
        return hour >= 22 || hour < 6
    }

    static func pickWater() -> String {
        isLateNight ? L10n.pick(lateNightWater.0, lateNightWater.1)
                    : L10n.pick(water.0, water.1)
    }

    static func pickMicro() -> String {
        isLateNight ? L10n.pick(lateNightMicro.0, lateNightMicro.1)
                    : L10n.pick(micro.0, micro.1)
    }

    /// Completed sit-break counts worth a celebration (parity with CelebrateAt).
    static let celebrateAt: [Int64] = [3, 5, 8]
}

enum CopyFormat {
    /// "45m" / "2h05m" (parity with MainViewModel.FormatMinutes).
    static func minutes(_ total: Int64) -> String {
        let total = max(0, total)
        return total >= 60 ? "\(total / 60)h\(String(format: "%02d", total % 60))m" : "\(total)m"
    }

    /// Localized duration for the big numbers ("2 hr 05 min" / "45 min").
    static func duration(_ total: Int64) -> String {
        let total = max(0, total)
        return total >= 60 ? L10n.t("AppHourMin", "\(total / 60)", String(format: "%02d", total % 60))
                           : L10n.t("AppMinutes", "\(max(total, 0))")
    }

    /// Compact axis label: "·" for zero (parity with FormatMinutesCompact).
    static func compact(_ total: Int64) -> String {
        let total = max(0, total)
        if total == 0 { return "·" }
        return total >= 60 ? "\(total / 60)h" : "\(total)m"
    }
}

/// Main-actor model bridging the embedded Racket backend and the host UI.
/// All policy (tick, scheduling, break lifecycle, merging, persistence) lives
/// in the backend; this only renders state and forwards user actions.
@MainActor
final class MoveBitModel: ObservableObject {
    @MainActor
    private final class EventRelay {
        weak var model: MoveBitModel?

        init(_ model: MoveBitModel) { self.model = model }

        func receive(_ event: RivetEvent) {
            model?.receive(event)
        }

        func ready(config: ReminderConfig, today: DayStats, paused: PauseState,
                   loops: [LoopInfo], breakActive: BreakState?) {
            model?.bootstrap(config: config, today: today, paused: paused,
                             loops: loops, breakActive: breakActive)
        }

        func fail(_ message: String) {
            guard let model else { return }
            model.ready = false
            model.status = message
        }
    }

    @Published var ready = false
    @Published var status = ""
    @Published var today = DayStats(date: "", active_mins: 0, sit_breaks: 0,
                                    water_reminders: 0, micro_breaks: 0,
                                    longest_session_mins: 0)
    @Published var config = ReminderConfig(
        sit_reminder_minutes: 45, water_reminder_minutes: 30, away_reset_minutes: 5,
        force_break_enabled: true, break_duration_minutes: 5, skip_after_seconds: 20,
        snooze_minutes: 10, micro_break_enabled: true, micro_break_interval_minutes: 30,
        micro_break_duration_seconds: 20, sound_enabled: true, auto_check_updates: true,
        welcome_shown: false, language: "auto")
    @Published var paused = PauseState(paused: false, until_ms: nil)
    @Published var loops: [LoopInfo] = []
    @Published var breakActive: BreakState?
    @Published var history: [DayStats] = []
    @Published var historyDays = 7
    @Published var autostart = false
    @Published var configStatus = ""
    @Published var dataDir = ""

    // Window-level requests handled by the AppDelegate (it owns the NSWindows).
    var onReady: (() -> Void)?
    var onConfigChanged: (() -> Void)?
    var onRemindersDue: ((RemindersDue) -> Void)?
    var onBreakStarted: (() -> Void)?
    var onBreakEnded: ((Bool) -> Void)?

    private var backend: EmbeddedRacketBackend?
    private var language = "auto"

    var resolvedLanguage: String {
        if language == "auto" {
            let preferred = Locale.preferredLanguages.first ?? "en"
            return preferred.hasPrefix("zh") ? "zh" : "en"
        }
        return language
    }

    func api() -> RivetAPI? {
        guard let backend else { return nil }
        return RivetAPI(client: backend.client)
    }

    func loop(_ kind: ReminderKind) -> LoopInfo? {
        loops.first { $0.kind == kind }
    }

    var isPaused: Bool { paused.paused }

    /// Sit-loop minutes accumulated in the current cycle — the closest host-side
    /// reading of "current continuous session" (it resets on break/away/snooze).
    var currentSessionMins: Int64 {
        loop(.sit)?.accumulated_mins ?? 0
    }

    // MARK: lifecycle

    func start() {
        guard backend == nil else { return }
        do {
            let config = try EmbeddedRacketConfiguration.resolvedDefault(
                moduleName: RivetGeneratedConfig.moduleName,
                entryName: RivetGeneratedConfig.entryName)
            let backend = EmbeddedRacketBackend(configuration: config)
            let relay = EventRelay(self)
            self.backend = backend
            status = "Starting embedded Racket CS…"
            Task.detached { [backend, relay] in
                do {
                    try backend.start { name, value in
                        guard let event = try? RivetEvent.decode(name: name, value: value) else { return }
                        Task { @MainActor in relay.receive(event) }
                    }
                    let api = RivetAPI(client: backend.client)
                    // initialize seeds states and starts the 30 s backend tick.
                    try await api.initialize()
                    async let config = api.getActive_config()
                    async let today = api.getToday()
                    async let paused = api.getPaused()
                    async let loops = api.getLoops()
                    async let breakActive = api.getBreak_active()
                    let (c, t, p, l, b) = try await (config, today, paused, loops, breakActive)
                    await relay.ready(config: c, today: t, paused: p, loops: l, breakActive: b)
                } catch {
                    await relay.fail(String(describing: error))
                }
            }
        } catch {
            status = "Configuration error: \(error)"
        }
    }

    private func bootstrap(config: ReminderConfig, today: DayStats, paused: PauseState,
                           loops: [LoopInfo], breakActive: BreakState?) {
        self.config = config
        self.today = today
        self.paused = paused
        self.loops = loops
        self.breakActive = breakActive
        language = config.language
        L10n.language = resolvedLanguage
        ready = true
        status = ""

        Task { await reloadHistory() }
        Task { await reloadAutostart() }
        Task { await reloadDataDir() }
        onReady?()
    }

    // MARK: events

    private func receive(_ event: RivetEvent) {
        switch event {
        case .tick_stats(let stats):
            today = stats
            // The tick thread also republishes loops/paused/break-active states.
            Task {
                if let api = api() {
                    if let l = try? await api.getLoops() { loops = l }
                    if let p = try? await api.getPaused() { paused = p }
                    if let b = try? await api.getBreak_active() { breakActive = b }
                }
            }
        case .reminders_due(let due):
            onRemindersDue?(due)
        case .break_started(let started):
            // ends-at / skip-after / started-at come from the authoritative
            // state; if the state read races ahead of the publish, fall back
            // to the event payload so the lock is never silently skipped.
            Task {
                var state = try? await api()?.getBreak_active()
                if state == nil {
                    let now = Int64(Date().timeIntervalSince1970 * 1000)
                    state = BreakState(ends_at_ms: now + started.duration_ms,
                                       skip_after_ms: started.skip_after_ms,
                                       started_at_ms: now)
                }
                if let state {
                    breakActive = state
                    onBreakStarted?()
                }
            }
        case .break_ended(let ended):
            breakActive = nil
            onBreakEnded?(ended.completed)
        case .day_completed(let stats):
            today = stats
            Task { await reloadHistory() }
        case .config_changed(let changed):
            applyConfig(changed, announce: false)
        }
    }

    // MARK: state reloads

    func reloadHistory() async {
        guard let api = api() else { return }
        // Always fetch 30 days; the 7/30 toggle is pure client-side slicing.
        history = (try? await api.get_history(days: 30)) ?? history
    }

    private func reloadAutostart() async {
        guard let api = api() else { return }
        autostart = (try? await api.get_autostart()) ?? false
    }

    /// The config-file path footer comes from the diagnostics text block.
    private func reloadDataDir() async {
        guard let api = api(), let text = try? await api.get_diagnostics() else { return }
        for line in text.split(separator: "\n") {
            if line.hasPrefix("data-dir: ") {
                dataDir = String(line.dropFirst("data-dir: ".count))
            }
        }
    }

    // MARK: config

    private func applyConfig(_ changed: ReminderConfig, announce: Bool) {
        let languageChanged = changed.language != config.language
        config = changed
        language = changed.language
        L10n.language = resolvedLanguage
        if languageChanged || announce {
            onConfigChanged?()
        }
    }

    /// Optimistic save; the backend clamps and re-announces via config-changed.
    func saveConfig(_ draft: ReminderConfig) {
        guard let api = api() else { return }
        let previous = config
        config = draft
        language = draft.language
        L10n.language = resolvedLanguage
        Task {
            do {
                try await api.set_config(value: draft)
                configStatus = L10n.t("SetStatusSaved", Date())
            } catch {
                config = previous
                language = previous.language
                L10n.language = resolvedLanguage
                configStatus = L10n.t("SetStatusSaveFailed")
            }
        }
    }

    func setAutostart(_ enabled: Bool) {
        guard let api = api() else { return }
        let previous = autostart
        autostart = enabled
        Task {
            do {
                try await api.set_autostart(enabled: enabled)
                configStatus = L10n.t("SetStatusApplied", Date())
            } catch {
                autostart = previous
                configStatus = L10n.t("SetStatusSaveFailed")
            }
        }
    }

    // MARK: reminder actions

    func skipBreak() {
        guard let api = api() else { return }
        Task { try? await api.skip_break() }
    }

    /// Host countdown reached zero: the scheduler stays authoritative, this only
    /// nudges it (no-op when no break runs).
    func completeBreak() {
        guard let api = api() else { return }
        Task { try? await api.complete_break() }
    }

    func snooze(_ kinds: [ReminderKind]) {
        guard let api = api() else { return }
        Task {
            for kind in kinds {
                try? await api.snooze(kind: kind)
            }
            if let l = try? await api.getLoops() { loops = l }
        }
    }

    func togglePause() {
        guard let api = api() else { return }
        Task {
            if paused.paused {
                try? await api.resume()
            } else {
                try? await api.pause_1h()
            }
            if let p = try? await api.getPaused() { paused = p }
            onConfigChanged?()
        }
    }

    /// Best-effort flush on exit (port of FlushToday-on-exit).
    func flushNowSync() {
        guard let api = api() else { return }
        let semaphore = DispatchSemaphore(value: 0)
        let apiCopy = api
        Task.detached {
            try? await apiCopy.flush_now()
            semaphore.signal()
        }
        _ = semaphore.wait(timeout: .now() + 1)
    }
}
