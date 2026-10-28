// Host-side i18n loader over Rivet's GeneratedI18n table (issue #66).
// Same call surface the SwiftUI host has always used — L10n.t(key, args…)
// and L10n.pick(prefix, count) with Date-aware placeholder substitution —
// but keyed by the dotted keys from the project's shared/i18n single
// source, which `raco rivet i18n` verifies and embeds; the hand-grown
// PascalCase tables are gone.

import Foundation

enum L10n {
    nonisolated(unsafe) static var language: String = "zh"

    private static let formatPatterns = ["yyyy-MM-dd", "HH:mm:ss", "HH:mm"]

    private static func formatted(_ date: Date, pattern: String) -> String {
        let formatter = DateFormatter()
        formatter.dateFormat = pattern
        formatter.timeZone = .current
        return formatter.string(from: date)
    }

    private static func string(of arg: Any) -> String {
        if let date = arg as? Date { return formatted(date, pattern: "yyyy-MM-dd") }
        return String(describing: arg)
    }

    /// Look up a key in the active language, falling back to the other
    /// language then the key itself — Rivet's own loader contract.
    /// "{0}"-style placeholders are substituted from the args; a Date arg
    /// also fills the "{0:pattern}" variants before the plain token.
    static func t(_ key: String, _ args: Any...) -> String {
        var s = GeneratedI18n.tr(language, key)
        if s == key { return key }
        for (i, arg) in args.enumerated() {
            if let date = arg as? Date {
                for pattern in formatPatterns {
                    let token = "{\(i):\(pattern)}"
                    if s.contains(token) {
                        s = s.replacingOccurrences(of: token, with: formatted(date, pattern: pattern))
                    }
                }
            }
            s = s.replacingOccurrences(of: "{\(i)}", with: string(of: arg))
        }
        return s
    }

    /// Picks one variation from a dotted pool prefix: pick("copy.microPool", 9)
    /// resolves e.g. "copy.microPool.3".
    static func pick(_ prefix: String, _ count: Int) -> String {
        guard count > 0 else { return t(prefix) }
        return t(prefix + "." + String(Int.random(in: 0..<count)))
    }
}
