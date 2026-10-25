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
        // Pre-bootstrap placeholder = the gentle shipped default; the backend
        // overwrites it with the real persisted config right after init.
        force_break_enabled: false, break_duration_minutes: 5, skip_after_seconds: 20,
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
    /// The restart-install handoff: after the backend staged the payload and
    /// spawned the apply helper, the host must quit so the helper can swap
    /// the bundle and relaunch.
    var onQuitRequested: (() -> Void)?

    private var backend: EmbeddedRacketBackend?
    private var language = "auto"
    /// An emergency exit whose skip RPC never reached the backend; flushed on
    /// the next live tick so the break ends without re-locking the displays.
    private var pendingBreakSkip = false

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
                    // The updater stages its payload into the install root
                    // (the folder holding movebit.app) — hand it over before
                    // anything can start a download.
                    try? await api.set_install_dir(
                        path: Bundle.main.bundleURL.deletingLastPathComponent()
                            .path)
                    async let config = api.get_active_config()
                    async let today = api.get_today()
                    async let paused = api.get_paused()
                    async let loops = api.get_loops()
                    async let breakActive = api.get_break_active()
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
        Task { reflectPendingUpdate() }
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
                    if let l = try? await api.get_loops() { loops = l }
                    if let p = try? await api.get_paused() { paused = p }
                    if let b = try? await api.get_break_active() { breakActive = b }
                    reconcilePendingBreakSkip()
                }
            }
        case .reminders_due(let due):
            onRemindersDue?(due)
        case .break_started(let started):
            // ends-at / skip-after / started-at come from the authoritative
            // state; if the state read races ahead of the publish, fall back
            // to the event payload so the lock is never silently skipped.
            Task {
                var state = try? await api()?.get_break_active()
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

    /// Emergency exit: the host already tore the cover down locally, so the
    /// displays are usable no matter what happens here. This only tells the
    /// backend (skip semantics = snooze sit, no instant re-lock); when the
    /// backend is unreachable the intent is remembered and flushed by the
    /// next live tick (reconcilePendingBreakSkip).
    func emergencySkipBreak() {
        guard let api = api() else {
            pendingBreakSkip = true
            return
        }
        Task { [weak self] in
            guard let self else { return }
            do {
                try await api.skip_break()
                self.pendingBreakSkip = false
            } catch {
                self.pendingBreakSkip = true
            }
        }
    }

    /// A live tick proves the backend is back: finish a remembered emergency
    /// exit — retry the skip while the backend still holds the break, drop it
    /// once the break has ended on its own. Skip on a finished break is a
    /// harmless no-op backend-side.
    private func reconcilePendingBreakSkip() {
        guard pendingBreakSkip else { return }
        if breakActive != nil {
            emergencySkipBreak()
        } else {
            pendingBreakSkip = false
        }
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
            if let l = try? await api.get_loops() { loops = l }
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
            if let p = try? await api.get_paused() { paused = p }
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

    // MARK: updates (backend-driven state machine)

    /// Update state machine, mirroring the Windows host's controller and the
    /// backend's snapshot phases: idle → checking → (available | upToDate |
    /// failed) → downloading → (cancelled | ready) → applying. The backend
    /// owns verify + download (signed feed) and the staged swap; this is
    /// presentation only.
    enum UpdatePhase: Equatable {
        case idle
        case checking
        case available
        case downloading
        case cancelled
        case ready
        case applying
        case upToDate
        case failed
    }

    @Published var updatePhase: UpdatePhase = .idle
    @Published var updateAvailableVersion: String?
    @Published var updatePercent: Int64 = 0
    /// Raw backend failure text — the About card shows it selectable so a
    /// bug report starts from the real message (family UPDATE.md rule).
    @Published var updateErrorText: String?

    private var updatePollTask: Task<Void, Never>?
    /// Wall-clock start of the current download — the host-side 30-minute
    /// watchdog reads it (nil whenever no download is in flight).
    private var updateDownloadStartedAt: Date?

    /// Manual entry (About card ▸ Check for Updates). Every outcome is
    /// visible — up to date and failures included.
    func checkForUpdates() {
        guard updatePhase != .checking, updatePhase != .downloading,
              updatePhase != .applying else { return }
        guard let api = api() else { return }
        updatePollTask?.cancel()
        updatePhase = .checking
        updateErrorText = nil
        Task { [weak self] in
            guard let self else { return }
            do {
                let check = try await api.check_updates()
                self.handleUpdateCheck(check)
            } catch {
                self.updatePhase = .failed
                self.updateErrorText = Self.cleanError(String(describing: error))
            }
        }
    }

    private func handleUpdateCheck(_ check: RivetTypes.UpdateCheck) {
        switch check.status {
        case "available":
            updatePhase = .available
            updateAvailableVersion = check.available_version
            updateErrorText = nil
        case "up-to-date":
            updatePhase = .upToDate
            updateAvailableVersion = nil
            updateErrorText = nil
        default:
            updatePhase = .failed
            updateErrorText = check.error ?? L10n.t("UpdCheckFailed")
        }
    }

    /// One-shot snapshot read at startup: the backend's silent auto-check
    /// (startup + every 6 h) leaves its outcome in the state box; surface a
    /// pending update without nagging about anything else.
    func reflectPendingUpdate() {
        guard updatePhase == .idle, let api = api() else { return }
        Task { [weak self] in
            guard let self, let state = try? await api.get_update_state() else { return }
            guard self.updatePhase == .idle else { return }
            if state.phase == "idle", let version = state.available_version {
                self.updatePhase = .available
                self.updateAvailableVersion = version
            }
        }
    }

    /// Consent given: the backend downloads against the signed manifest while
    /// the UI polls the state snapshot.
    func startUpdateDownload() {
        guard updatePhase == .available || updatePhase == .cancelled,
              let api = api() else { return }
        updatePhase = .downloading
        updatePercent = 0
        updateErrorText = nil
        updateDownloadStartedAt = Date()
        Task { [weak self] in
            guard let self else { return }
            do {
                try await api.start_update_download()
                self.pollUpdateState()
            } catch {
                self.updatePhase = .failed
                self.updateErrorText = Self.cleanError(String(describing: error))
            }
        }
    }

    /// The start RPC doubles as the cancel gesture (the backend's toggle
    /// contract): calling it while a download runs cancels that download and
    /// the poll loop observes the idle phase. The card then offers the
    /// update again instead of reporting a failure — a cancel is not one.
    func cancelUpdateDownload() {
        guard updatePhase == .downloading, api() != nil else { return }
        Task { [weak self] in
            guard let self, let api = self.api() else { return }
            _ = try? await api.start_update_download()
        }
    }

    /// 700 ms poll while downloading; terminal phases leave the loop. The
    /// 30-minute ceiling is the host-side backstop: the backend's own
    /// watchdog (stall 120 s / total 30 min) normally lands the terminal
    /// state first, but if the backend itself is wedged this forces the UI
    /// out of an endless progress bar.
    private func pollUpdateState() {
        updatePollTask?.cancel()
        updatePollTask = Task { [weak self] in
            let deadline = Date().addingTimeInterval(30 * 60)
            while !Task.isCancelled {
                guard let self, let api = self.api() else { return }
                if let state = try? await api.get_update_state() {
                    self.applyUpdateState(state)
                }
                if Date() > deadline, self.updatePhase == .downloading {
                    self.cancelUpdateDownload()
                    self.updatePollTask?.cancel()
                    self.updateDownloadStartedAt = nil
                    self.updatePhase = .failed
                    self.updateErrorText = L10n.t("UpdCheckFailed")
                    return
                }
                try? await Task.sleep(nanoseconds: 700_000_000)
            }
        }
    }

    private func applyUpdateState(_ state: RivetTypes.UpdateState) {
        guard updatePhase == .downloading else { return }
        updatePercent = state.percent
        if let version = state.available_version {
            updateAvailableVersion = version
        }
        switch state.phase {
        case "downloaded":
            updatePollTask?.cancel()
            updateDownloadStartedAt = nil
            updatePhase = .ready
            updateErrorText = nil
        case "idle":
            // The cancel gesture landed: back to the offer, not a failure.
            updatePollTask?.cancel()
            updateDownloadStartedAt = nil
            updatePhase = .cancelled
            updateErrorText = nil
        case "error":
            updatePollTask?.cancel()
            updateDownloadStartedAt = nil
            updatePhase = .failed
            updateErrorText = state.message ?? L10n.t("UpdCheckFailed")
        default:
            break
        }
    }

    /// 重启并安装: the backend stages the payload and spawns the detached
    /// helper; this host's only remaining job is to quit so the helper can
    /// swap the bundle and relaunch.
    func applyStagedUpdate() {
        guard updatePhase == .ready, let api = api() else { return }
        updatePhase = .applying
        updatePollTask?.cancel()
        Task { [weak self] in
            guard let self else { return }
            do {
                _ = try await api.prepare_update_install()
                self.onQuitRequested?()
            } catch {
                self.updatePhase = .failed
                self.updateErrorText = Self.cleanError(String(describing: error))
            }
        }
    }

    /// The About card's button IS the state machine (parity with Windows):
    /// check → download → restart-install, retry on failure.
    func handleUpdateButton() {
        switch updatePhase {
        case .available, .cancelled:
            startUpdateDownload()
        case .ready:
            applyStagedUpdate()
        default:
            checkForUpdates()
        }
    }

    static func cleanError(_ text: String) -> String {
        // RVT1 failures arrive as "...error: <message>"; keep the message.
        if let range = text.range(of: "error: ") {
            return String(text[range.upperBound...])
        }
        return text
    }
}
