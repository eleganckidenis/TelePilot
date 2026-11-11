# Automatic buffering in 2.1.0

Windows and native macOS use the same bounded recovery policy. The shared
`testdata/adaptive-buffer-traces.json` fixtures exercise both implementations;
their runners can export every actual decision with `TELEPILOT_TRACE_REPORT`.

## Cache and learning

The initial input really receives 2 seconds of network cache. The internal range
is 1–30 seconds. Desired cache and cache applied to the current input are separate:
changes take effect on the next necessary reopen. Healthy playback is never
restarted merely to change latency. The HLS bitrate cap remains optional.

Only demonstrated playback followed by a meaningful interruption teaches the
policy about starvation. Finished buffering episodes of at least 0.75 seconds
update an exponentially weighted interruption estimate:

```
risk = 0.6 * risk + 0.4 * min(interruptionSeconds, 20)
desired = clamp(max(desired * 1.5, applied + 2, risk * 2 + 1), 1, 30)
```

Three such episodes within 30 seconds can request recovery, with a 20-second
cooldown and at most 16 retained episode timestamps. Prolonged rebuffering waits
`max(8, applied + 4)` seconds; initial/refill waiting allows
`max(8, applied + 6)`. Every 60 seconds of continuously observed healthy playback,
desired cache is reduced by 15%, without reopening the current input.

## Output evidence and recovery

Video displayed and audio played counters are watched independently after two
positive increments. Decoding or receiving bytes cannot conceal an established
output freeze. Eight seconds without established output requests recovery. Cache
grows only when previously advancing input has also stopped for at least three
seconds. Decoder/output trouble with advancing or unknown input keeps the current
desired cache. A cumulative media-clock window is a fallback for unavailable
output counters. The first positive output is progress even before watchdog
arming. Negative/unavailable counters cannot suppress another valid output.

An intentionally hidden/minimized video surface, disabled audio, radio artwork,
pause, seek, track changes, resets and observation gaps receive separate handling.
Duplicate, backward or nonfinite observation times cannot earn health or retries.
A gap over five seconds cannot count as healthy time. Recent actual progress
remains evidence for up to three seconds between polls.

For positively known, enabled and visible video, two positive decoded deltas
while the displayed counter remains zero establish a conservative first-frame
watch. Recovery waits `max(30, applied + 6)` seconds from that evidence. Unknown
tracks, default/missing stats and radio cannot establish this watch. Pending first
presentation cannot replenish the retry budget. This accommodates keyframe waits
without accepting a permanently blank video as healthy.

All failure recovery uses one six-retry budget, with delays of
0.4, 2, 4, 8, 8 and 8 seconds. Only 120 seconds of continuously observed healthy
output can reset it. `Playing` notifications and elapsed wall time cannot reset
it. Independently, 90 seconds without actual progress ends an unsuccessful
session, including pending work. Explicit pause suspends this deadline; resume
starts a fresh observation window while retaining the retry count.

Native adapters retain the current input's `EncounteredError` event. VLC 3.0.24
can overwrite its Error state with Ended when the input thread dies, including
after HTTP 503. Such an input is passed to recovery as Error, not genuine EOF.
Clean EOF also requires the current input's `EndReached` event, allowing even
files shorter than the polling interval to finish correctly. Raw Ended without
either event receives at most one second for event delivery, then becomes a
failed-input retry. Error evidence takes precedence. The latches are cleared
after stopping the old input and before opening the next one; callbacks do not
invoke blocking VLC APIs. Finite EOF without an error still ends playback
immediately.

EOF, exhausted retries and the
no-progress deadline release playback, cancel old work and return focus and
selection to the channel list. They do not quit the app or stop independent
recording. Session/attempt guards prevent old callbacks, seeks and track dialogs
from affecting replacement playback. Recovery preserves mute, volume, pause
intent, matching tracks and absolute archive position.

## Compatibility and limits

Old manual time settings, even malformed ones, are ignored. Other preferences,
including the HLS cap, survive migration; newly saved settings omit manual times.

This is network-cache tuning and bounded recovery, not a local DVR, a throughput
or buffer-occupancy measurement, or playback-rate control. LibVLC's displayed
counter can include redisplays and its decoded counter can include successful
decoder calls while waiting for a keyframe. These counters are engine evidence,
not a proof that every picture contains fresh content or that audio was audible.
No algorithm can repair missing source content or guarantee an uninterrupted
provider connection.

Primary implementation references: [LibVLC media options](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.Media.html),
[VLC 3.0.24 decoder accounting](https://github.com/videolan/vlc/blob/3.0.24/src/input/decoder.c),
[video output accounting](https://github.com/videolan/vlc/blob/3.0.24/src/video_output/video_output.c).
The Error-to-Ended distinction follows
[LibVLC's input state/death handling](https://github.com/videolan/vlc/blob/3.0.24/lib/media_player.c).
