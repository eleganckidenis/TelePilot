namespace TelePilot.Tests.Playback;

// The application owns one native player; native integration scenarios must
// also run one at a time rather than loading concurrent VLC plugin instances.
[CollectionDefinition(nameof(NativePlaybackTestGroup), DisableParallelization = true)]
public sealed class NativePlaybackTestGroup;
