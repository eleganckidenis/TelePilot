import Testing
@testable import TelePilotMac

@MainActor struct ApplicationTerminationTests {
    @Test func repeatedQuitWaitsForTheOriginalShutdown() async {
        let termination = ApplicationTermination()
        var continuation: CheckedContinuation<Bool, Never>?
        var prepares = 0
        var replies: [Bool] = []
        termination.request(prepare: {
            prepares += 1
            return await withCheckedContinuation { continuation = $0 }
        }, reply: { replies.append($0) })
        while continuation == nil { await Task.yield() }
        let repeated = termination.request(prepare: {
            prepares += 1
            return false
        }, reply: { replies.append($0) })
        #expect(!repeated)
        #expect(prepares == 1)
        #expect(replies.isEmpty)
        continuation?.resume(returning: true)
        while !replies.contains(true) { await Task.yield() }
        #expect(replies == [true])
        let afterApproval = termination.request(prepare: { false }, reply: { replies.append($0) })
        #expect(!afterApproval)
    }

    @Test func cancelledQuitCanBeRetried() async {
        let termination = ApplicationTermination()
        var replies: [Bool] = []
        termination.request(prepare: { false }, reply: { replies.append($0) })
        while replies.isEmpty { await Task.yield() }
        termination.request(prepare: { true }, reply: { replies.append($0) })
        while replies.count < 2 { await Task.yield() }
        #expect(replies == [false, true])
    }
}
