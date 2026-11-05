import AppKit

@MainActor func button(_ title: String, _ target: AnyObject, _ action: Selector) -> NSButton {
    let b = NSButton(title: title, target: target, action: action)
    b.bezelStyle = .rounded; b.refusesFirstResponder = false
    b.setAccessibilityLabel(title); return b
}
@MainActor func popup(_ label: String, items: [String], target: AnyObject?, action: Selector?) -> NSPopUpButton {
    let p = NSPopUpButton(frame: .zero, pullsDown: false)
    p.addItems(withTitles: items); p.target = target; p.action = action
    p.setAccessibilityLabel(label); return p
}
@MainActor func stack(_ views: [NSView], vertical: Bool = false) -> NSStackView {
    let s = NSStackView(views: views); s.orientation = vertical ? .vertical : .horizontal
    s.alignment = vertical ? .leading : .centerY; s.spacing = 10
    return s
}
@MainActor func label(_ value: String) -> NSTextField { NSTextField(labelWithString: value) }
@MainActor func namedField(_ name: String, value: String = "") -> NSTextField {
    let f = NSTextField(string: value); f.setAccessibilityLabel(name); return f
}
@MainActor func fill(_ child: NSView, in parent: NSView, margin: CGFloat = 0) {
    child.translatesAutoresizingMaskIntoConstraints = false; parent.addSubview(child)
    NSLayoutConstraint.activate([
        child.leadingAnchor.constraint(equalTo: parent.leadingAnchor, constant: margin),
        child.trailingAnchor.constraint(equalTo: parent.trailingAnchor, constant: -margin),
        child.topAnchor.constraint(equalTo: parent.topAnchor, constant: margin),
        child.bottomAnchor.constraint(equalTo: parent.bottomAnchor, constant: -margin)
    ])
}
@MainActor func announce(_ text: String, window: NSWindow?) {
    guard let window else { return }
    NSAccessibility.post(element: window, notification: .announcementRequested,
        userInfo: [.announcement: text, .priority: NSAccessibilityPriorityLevel.medium.rawValue])
}
@MainActor func alert(_ title: String, _ detail: String, window: NSWindow? = nil) {
    let a = NSAlert(); a.messageText = title; a.informativeText = detail
    a.addButton(withTitle: "OK")
    let previous = window?.firstResponder
    a.runModal()
    if let previous { window?.makeFirstResponder(previous) }
}
@MainActor func form(_ title: String, views: [NSView], confirm: String = l("Zapisz", "Save")) -> Bool {
    let a = NSAlert(); a.messageText = title
    a.addButton(withTitle: confirm); a.addButton(withTitle: l("Anuluj", "Cancel"))
    a.buttons[0].keyEquivalent = "\r"; a.buttons[1].keyEquivalent = "\u{1b}"
    let body = stack(views, vertical: true); body.frame = NSRect(x: 0, y: 0, width: 490, height: CGFloat(views.count) * 38 + 8)
    body.widthAnchor.constraint(equalToConstant: 490).isActive = true
    for v in views { v.widthAnchor.constraint(equalTo: body.widthAnchor).isActive = true }
    a.accessoryView = body
    a.layout()
    if let initial = views.first(where: { v in
        if let text = v as? NSTextField { return text.isEditable }
        return v is NSControl
    }) { a.window.initialFirstResponder = initial }
    return a.runModal() == .alertFirstButtonReturn
}

@MainActor final class ChannelTable: NSTableView {
    var activate: (() -> Void)?
    var jump: ((String) -> Void)?
    override func keyDown(with event: NSEvent) {
        if event.keyCode == 109 && event.modifierFlags.contains(.shift), let menu {
            let row = max(0, selectedRow); menu.popUp(positioning: nil, at: NSPoint(x: 20, y: rect(ofRow: row).midY), in: self); return
        }
        if event.keyCode == 36 || event.keyCode == 76 { activate?(); return }
        if event.modifierFlags.intersection([.command,.control,.option]).isEmpty,
           let text = event.characters, text.count == 1, text.unicodeScalars.allSatisfy({ CharacterSet.letters.contains($0) }) {
            jump?(text); return
        }
        super.keyDown(with: event)
    }
}

@MainActor final class FlippedDocumentStack: NSStackView { override var isFlipped: Bool { true } }

func searchKey(_ value: String) -> String {
    value.replacingOccurrences(of: "ł", with: "l").replacingOccurrences(of: "Ł", with: "L").folding(options: [.diacriticInsensitive, .caseInsensitive], locale: .current)
}
