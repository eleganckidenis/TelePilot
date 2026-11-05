import AppKit
import os

enum Language: String, Codable, CaseIterable {
    case system, polish, english

    var title: String {
        switch self {
        case .system: l("Język systemu", "System language")
        case .polish: "Polski"
        case .english: "English"
        }
    }

    func resolve(preferredLanguages: [String] = Locale.preferredLanguages) -> Language {
        guard self == .system else { return self }
        for identifier in preferredLanguages {
            switch Locale(identifier: identifier).language.languageCode?.identifier {
            case "pl": return .polish
            case "en": return .english
            default: continue
            }
        }
        return .english
    }

    @MainActor static func chooseForFirstRun() -> Language {
        let picker = popup(l("Język aplikacji", "App language"),
                           items: allCases.map(\.title), target: nil, action: nil)
        let accepted = form(l("Wybierz język TelePilota", "Choose TelePilot’s language"),
                            views: [picker], confirm: l("Dalej", "Continue"))
        return accepted ? allCases[picker.indexOfSelectedItem] : .system
    }
}

enum Localization {
    // Player can produce track names on its own actor.
    private static let current = OSAllocatedUnfairLock(initialState: Language.system.resolve())

    static func configure(_ preference: Language) {
        current.withLock { $0 = preference.resolve() }
    }

    static var language: Language { current.withLock { $0 } }

    static var locale: Locale {
        let system = Locale.current
        guard system.language.languageCode?.identifier != (language == .polish ? "pl" : "en") else { return system }
        return Locale(identifier: language == .polish ? "pl_PL" : "en")
    }
}

func l(_ polish: @autoclosure () -> String, _ english: @autoclosure () -> String) -> String {
    Localization.language == .polish ? polish() : english()
}
