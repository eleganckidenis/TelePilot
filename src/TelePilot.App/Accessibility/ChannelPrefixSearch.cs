using TelePilot.App.ViewModels;
using TelePilot.Core.Channels;

namespace TelePilot.App.Accessibility;

internal sealed class ChannelPrefixSearch(TimeProvider? timeProvider = null)
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private string prefix = string.Empty;
    private long lastInput;
    private ChannelListItemViewModel? lastSelection;

    public void Reset()
    {
        prefix = string.Empty;
        lastSelection = null;
    }

    public ChannelListItemViewModel? Find(
        string text,
        IReadOnlyList<ChannelListItemViewModel> channels,
        ChannelListItemViewModel? selected,
        bool backspace = false)
    {
        var now = clock.GetTimestamp();
        if (clock.GetElapsedTime(lastInput, now) >= Timeout || !ReferenceEquals(selected, lastSelection))
        {
            Reset();
        }

        bool includeSelected = prefix.Length > 0;
        if (backspace)
        {
            if (prefix.Length == 0)
            {
                return null;
            }

            var starts = System.Globalization.StringInfo.ParseCombiningCharacters(prefix);
            prefix = prefix[..starts[^1]];
        }
        else
        {
            // Repeating one letter cycles matches; a different character extends
            // the prefix. TextInput also preserves keyboard layout and AltGr.
            bool cycle = prefix.Length == 1 && text.Length == 1
                && string.Equals(prefix, text, StringComparison.OrdinalIgnoreCase);
            includeSelected &= !cycle;
            prefix = cycle ? text : prefix + text;
        }

        lastInput = now;
        lastSelection = selected;
        var normalized = ChannelTextNormalizer.NormalizeForSearch(prefix);
        if (normalized.Length == 0 || channels.Count == 0)
        {
            return null;
        }

        int selectedIndex = -1;
        for (int index = 0; index < channels.Count; index++)
        {
            if (ReferenceEquals(channels[index], selected))
            {
                selectedIndex = index;
                break;
            }
        }

        int first = includeSelected && selectedIndex >= 0 ? 0 : 1;
        for (int offset = first; offset < channels.Count + first; offset++)
        {
            var candidate = channels[(selectedIndex + offset + channels.Count) % channels.Count];
            if (ChannelTextNormalizer.NormalizeForSearch(candidate.Name)
                .StartsWith(normalized, StringComparison.Ordinal))
            {
                lastSelection = candidate;
                return candidate;
            }
        }

        return null;
    }
}
