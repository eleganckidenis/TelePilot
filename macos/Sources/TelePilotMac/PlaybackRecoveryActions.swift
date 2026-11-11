import AppKit
import CFNetwork
import TelePilotCore

@MainActor extension MainController {
    func startPlaybackAttempt(_ url: URL, generation: Int, quickStart: Bool) {
        guard let policy = recovery else { return }
        playbackReady = false
        playbackAttempt += 1; let attempt = playbackAttempt
        let applied = policy.target
        let address = UInt(bitPattern: Unmanaged.passUnretained(video).toOpaque())
        var options = policy.settings.options(target: applied, live: policy.live, quickStart: quickStart)
        options += [":http-user-agent=\(PlaybackHTTP.userAgent)", ":http-header=Accept: \(PlaybackHTTP.accept)"]
        // LibVLC does not read macOS's manual HTTP proxy settings itself.
        if let proxy = CFNetworkCopySystemProxySettings()?.takeRetainedValue() as? [String: Any] {
            let secure = url.scheme?.lowercased() == "https"
            let prefix = secure && (proxy["HTTPSEnable"] as? NSNumber)?.boolValue == true ? "HTTPS" : "HTTP"
            if (proxy["\(prefix)Enable"] as? NSNumber)?.boolValue == true,
               let host = proxy["\(prefix)Proxy"] as? String, !host.isEmpty, let port = proxy["\(prefix)Port"] as? Int {
                options.append(":http-proxy=http://\(host):\(port)")
            }
        }
        let mediaOptions = options
        playbackTask = Task { [weak self] in
            guard let self else { return }
            let resolved = await HLSVariant.resolve(url, cap: policy.settings.variantMaxMbps)
            guard generation == playbackGeneration, attempt == playbackAttempt, !Task.isCancelled else { return }
            recovery?.beginAttempt(now: ProcessInfo.processInfo.systemUptime, applied: applied)
            do {
                try await player.play(resolved, viewAddress: address, generation: generation, attempt: attempt, options: mediaOptions)
                guard generation == playbackGeneration, attempt == playbackAttempt, !Task.isCancelled else { return }
                playbackReady = true; lastState = -1
                await player.volume(preferences.volume); await player.mute(preferences.muted)
            } catch {
                guard generation == playbackGeneration, attempt == playbackAttempt, !Task.isCancelled else { return }
                // The same bounded recovery handles a failed open and a later dropout.
                playbackReady = true
                let sample = PlaybackSample(state: 7)
                let action = recovery?.evaluate(sample, now: ProcessInfo.processInfo.systemUptime) ?? .lost
                await handleRecovery(action, sample: sample, generation: generation)
            }
        }
    }

    func handleRecovery(_ action: PlaybackRecovery.Action, sample: PlaybackSample, generation: Int) async {
        guard generation == playbackGeneration else { return }
        switch action {
        case .none: break
        case .finished:
            finishPlayback(status: l("Transmisja została zakończona.", "The stream has ended."), returnToChannels: true)
            await playbackTask?.value
        case .lost:
            finishPlayback(status: l("Nie udało się odzyskać transmisji. Wybierz kanał, aby spróbować ponownie.", "Could not recover the stream. Select a channel to try again."), returnToChannels: true)
            await playbackTask?.value
        case let .retry(delay, quickStart):
            guard let channel = playing, let original = playingURL else { return }
            let attempts = recovery?.attempts ?? 0
            let target = recovery?.target ?? 25
            setStatus(quickStart ? l("Odświeżanie transmisji: \(channel.name).", "Refreshing the stream: \(channel.name).") : l("Ponowne łączenie: \(channel.name), próba \(attempts) z 6. Bufor \(Int(target)) s.", "Reconnecting to \(channel.name), attempt \(attempts) of 6. Buffer: \(Int(target)) s."))
            recoveryTask?.cancel()
            recoveryTask = Task { [weak self] in
                do { try await Task.sleep(for: .seconds(delay)) } catch { return }
                guard let self, generation == playbackGeneration, !Task.isCancelled, recovery?.gaveUp == false else { return }
                var url = original
                if let programme = archiveProgramme, sample.time > 0 {
                    let offset = archiveOffset + sample.time
                    if let resumed = try? CatchUp.url(channel: channel, programme: programme, profile: profile(for: channel), offset: offset) {
                        url = resumed; playingURL = resumed; archiveOffset = offset
                    }
                }
                pendingPause = paused || pendingPause; paused = false
                trackRestoreTicks = 20
                startPlaybackAttempt(url, generation: generation, quickStart: quickStart)
            }
        }
    }
}
