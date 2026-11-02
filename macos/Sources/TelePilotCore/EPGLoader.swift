import Foundation

/// Loads a bounded XMLTV source and keeps decoding/parsing off the UI thread.
public enum EPGLoader {
    public static func load(from url: URL, matching channels: [Channel]? = nil) async throws -> TVGuide {
        try Task.checkCancellation()
        let data: Data
        if url.isFileURL {
            let reading = Task.detached(priority: .utility) {
                do {
                    try Task.checkCancellation()
                    let size = try url.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0
                    guard size <= XMLTVParser.maximumBytes else { throw TelePilotError.tooLarge }
                    let value = try Data(contentsOf: url, options: .mappedIfSafe)
                    guard value.count <= XMLTVParser.maximumBytes else { throw TelePilotError.tooLarge }
                    try Task.checkCancellation()
                    return value
                } catch let error as TelePilotError { throw error }
                catch is CancellationError { throw CancellationError() }
                catch { throw TelePilotError.sourceUnavailable }
            }
            data = try await withTaskCancellationHandler(operation: { try await reading.value }, onCancel: { reading.cancel() })
        } else {
            data = try await HTTPDataLoader.load(url, maximumBytes: XMLTVParser.maximumBytes)
        }
        try Task.checkCancellation()
        let parsing = Task.detached(priority: .utility) { try XMLTVParser.parse(data, matching: channels) }
        let guide = try await withTaskCancellationHandler(operation: { try await parsing.value }, onCancel: { parsing.cancel() })
        try Task.checkCancellation()
        return guide
    }
}
