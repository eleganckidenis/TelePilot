import AppKit
import TelePilotCore

@MainActor final class GuideBrowser: NSViewController, NSTableViewDataSource, NSTableViewDelegate, NSSearchFieldDelegate {
    weak var owner: MainController?
    let archive: Bool
    let channelPopup = NSPopUpButton()
    let date = NSDatePicker()
    let search = NSSearchField()
    let programmesTable = ChannelTable()
    let detail = NSTextView()
    let message = label("")
    let showAll = NSButton(checkboxWithTitle: "Pokaż także kanały bez informacji o archiwum", target: nil, action: nil)
    var channels: [Channel] = []
    var programmes: [Programme] = []
    init(owner: MainController, archive: Bool) { self.owner = owner; self.archive = archive; super.init(nibName: nil, bundle: nil) }
    required init?(coder: NSCoder) { fatalError("init(coder:) unavailable") }
    var channel: Channel? { channels.indices.contains(channelPopup.indexOfSelectedItem) ? channels[channelPopup.indexOfSelectedItem] : nil }
    var selectedProgramme: Programme? { programmes.indices.contains(programmesTable.selectedRow) ? programmes[programmesTable.selectedRow] : nil }
    override func loadView() {
        view = NSView()
        channelPopup.target = self; channelPopup.action = #selector(channelChanged); channelPopup.setAccessibilityLabel("Kanał")
        date.datePickerStyle = .textFieldAndStepper; date.datePickerElements = [.yearMonthDay]
        date.dateValue = .now; date.target = self; date.action = #selector(dayChanged); date.setAccessibilityLabel("Data programu")
        search.placeholderString = "Szukaj audycji"; search.setAccessibilityLabel("Szukaj audycji"); search.delegate = self
        showAll.target = self; showAll.action = #selector(allChanged)
        let column = NSTableColumn(identifier: .init("programme")); column.title = "Czas i tytuł audycji"; column.resizingMask = .autoresizingMask
        programmesTable.addTableColumn(column); programmesTable.headerView = nil; programmesTable.rowHeight = 32
        programmesTable.usesAlternatingRowBackgroundColors = true; programmesTable.dataSource = self; programmesTable.delegate = self
        programmesTable.setAccessibilityLabel(archive ? "Audycje archiwalne" : "Audycje")
        programmesTable.activate = { [weak self] in self?.playProgramme() }
        programmesTable.target = self; programmesTable.doubleAction = #selector(playProgramme)
        let scroll = NSScrollView(); scroll.documentView = programmesTable; scroll.hasVerticalScroller = true; scroll.borderType = .bezelBorder
        detail.isEditable = false; detail.isSelectable = true; detail.font = .systemFont(ofSize: 14)
        detail.setAccessibilityLabel("Szczegóły audycji")
        let detailScroll = NSScrollView(); detailScroll.documentView = detail; detailScroll.hasVerticalScroller = true; detailScroll.borderType = .bezelBorder
        detail.minSize = NSSize(width:0,height:100); detail.maxSize = NSSize(width:CGFloat.greatestFiniteMagnitude,height:CGFloat.greatestFiniteMagnitude)
        detail.isVerticallyResizable = true; detail.isHorizontallyResizable = false; detail.textContainer?.widthTracksTextView = true
        detailScroll.heightAnchor.constraint(equalToConstant: 130).isActive = true
        let dates = stack([date, button("Poprzedni dzień", self, #selector(previousDay)), button("Następny dzień", self, #selector(nextDay)), button("Dzisiaj", self, #selector(today))])
        let actions = stack([button(archive ? "Odtwórz z archiwum" : "Odtwórz kanał", self, #selector(playProgramme)), button("Zaplanuj nagranie…", self, #selector(schedule)), button("Odśwież EPG", self, #selector(refresh))])
        var views: [NSView] = [label(archive ? "Archiwum TV" : "Program telewizyjny"), channelPopup, dates]
        views.append(archive ? showAll : search)
        views += [message, scroll, detailScroll, actions]
        let body = stack(views, vertical: true)
        for child in [channelPopup,scroll,detailScroll,message] { child.widthAnchor.constraint(equalTo: body.widthAnchor).isActive = true }
        fill(body, in: view, margin: 8)
        scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 150).isActive = true
        scroll.setContentHuggingPriority(.defaultLow, for: .vertical)
    }
    func reloadChannels(select id: String? = nil) {
        loadViewIfNeeded(); guard let owner else { return }
        let previous = id ?? channel?.id
        channels = !archive ? owner.allChannels : owner.selectedSource.flatMap { owner.playlists[$0.id]?.channels } ?? []
        if archive && showAll.state == .off { channels = channels.filter { CatchUp.availability(channel: $0, profile: owner.profile(for: $0)).supported } }
        channelPopup.removeAllItems(); channelPopup.addItems(withTitles: channels.map(\.name))
        if let i = channels.firstIndex(where: { $0.id == previous }) { channelPopup.selectItem(at: i) }
        reloadProgrammes()
    }
    @objc func channelChanged() { reloadProgrammes() }
    @objc func allChanged() { reloadChannels() }
    @objc func dayChanged() { reloadProgrammes() }
    @objc func previousDay() { changeDay(-1) }
    @objc func nextDay() { changeDay(1) }
    func changeDay(_ delta: Int) { if let d = Calendar.current.date(byAdding: .day, value: delta, to: date.dateValue) { date.dateValue = d; reloadProgrammes() } }
    @objc func today() { date.dateValue = .now; reloadProgrammes() }
    @objc func refresh() { owner?.refreshEPG() }
    func controlTextDidChange(_ obj: Notification) { reloadProgrammes() }
    func reloadProgrammes() {
        guard isViewLoaded, let owner else { return }
        let selected = selectedProgramme?.id
        programmes = channel.map { owner.guide(for: $0).programmes(for: $0, on: date.dateValue) } ?? []
        if !archive && !search.stringValue.isEmpty { programmes = programmes.filter { $0.title.localizedStandardContains(search.stringValue) } }
        programmesTable.reloadData()
        if !programmes.isEmpty { programmesTable.selectRowIndexes(IndexSet(integer: programmes.firstIndex { $0.id == selected } ?? 0), byExtendingSelection: false) }
        if archive, let channel {
            let a = CatchUp.availability(channel: channel, profile: owner.profile(for: channel))
            message.stringValue = a.reason + (a.supported ? " Archiwum: \(Int(a.days ?? 0)) dni." : "")
        } else { message.stringValue = programmes.isEmpty ? "Brak danych EPG dla wybranego kanału i dnia." : "Audycje: \(programmes.count)." }
        detail.string = selectedProgramme?.detail ?? ""
    }
    func numberOfRows(in tableView: NSTableView) -> Int { programmes.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard programmes.indices.contains(row) else { return nil }; let p = programmes[row]
        let format = DateFormatter(); format.timeStyle = .short
        let hours = format.string(from: p.start) + " – " + (p.end.map { format.string(from: $0) } ?? "?")
        let current = p.start <= Date() && (p.end ?? .distantPast) > Date()
        let text = hours + " — " + p.title + (current ? " — teraz" : "")
        let cell = label(text); cell.lineBreakMode = .byTruncatingTail; cell.setAccessibilityLabel(text); return cell
    }
    func tableViewSelectionDidChange(_ notification: Notification) { detail.string = selectedProgramme?.detail ?? "" }
    @objc func playProgramme() {
        guard let channel else { return }
        if archive { if let p = selectedProgramme { owner?.playArchive(channel, programme: p) } }
        else { owner?.play(channel) }
        if !archive { view.window?.close() }
    }
    @objc func schedule() { guard let channel else { return }; owner?.scheduleRecording(channel: channel, programme: selectedProgramme) }
    override func cancelOperation(_ sender: Any?) {
        if !archive { view.window?.close(); owner?.restoreActiveFocus() }
    }
}
