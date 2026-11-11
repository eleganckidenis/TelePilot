using System.Text.Json;
using TelePilot.Core.Models;
using TelePilot.Playback;

namespace TelePilot.Tests.Playback;

public sealed class AdaptiveBufferTraceTests
{
    private static readonly object ReportGate = new();
    private static readonly SortedDictionary<string, List<object>> Reports = new(StringComparer.Ordinal);
    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "testdata", "adaptive-buffer-traces.json");

    public static IEnumerable<object[]> Cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        foreach (var trace in document.RootElement.GetProperty("cases").EnumerateArray())
            yield return [trace.GetProperty("name").GetString()!, trace.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SharedBehavioralTrace(string name, string json)
    {
        using var document = JsonDocument.Parse(json);
        var trace = document.RootElement;
        var policy = new PlaybackRecoveryPolicy(new PlaybackOptions(IsLive: trace.GetProperty("live").GetBoolean()),
            new Uri(trace.GetProperty("url").GetString()!), false, 0);
        var index = 0;
        var report = new List<object>();
        foreach (var step in trace.GetProperty("steps").EnumerateArray())
        {
            var stepIndex = index++;
            var now = step.GetProperty("now").GetDouble();
            var operation = step.GetProperty("op").GetString();
            if (operation == "begin")
            {
                policy.BeginAttempt(now, Number(step, "applied", policy.Target));
                Record(report, name, stepIndex, now, operation, default, policy);
                continue;
            }
            if (operation == "reset")
            {
                policy.ResetObservation(now);
                Record(report, name, stepIndex, now, operation, default, policy);
                continue;
            }
            PlaybackProgress? progress = null;
            if (step.TryGetProperty("progress", out var stats))
                progress = new(stats.GetProperty("video").GetInt64(), stats.GetProperty("displayed").GetInt64(),
                    stats.GetProperty("audio").GetInt64(), stats.GetProperty("played").GetInt64(),
                    stats.TryGetProperty("inputBytes", out var input) && input.ValueKind != JsonValueKind.Null ? input.GetInt64() : null);
            var action = policy.Evaluate(new RecoverySample(step.GetProperty("state").GetInt32(), Number(step, "position", -1),
                Number(step, "length", -1), progress, Flag(step, "audioOnly", false), Flag(step, "audioEnabled", true),
                Flag(step, "videoEnabled", true),
                step.TryGetProperty("knownVideoTrackCount", out var count) && count.ValueKind != JsonValueKind.Null ? count.GetInt32() : null), now, Flag(step, "paused", false));
            Record(report, name, stepIndex, now, operation!, action, policy);
            var expect = step.GetProperty("expect");
            var context = $"{name} sample {stepIndex} t={now}: {action.Kind}, target={policy.Target}, applied={policy.Applied}, attempts={policy.Attempts}";
            Assert.True(string.Equals(expect.GetProperty("kind").GetString(), action.Kind.ToString(), StringComparison.OrdinalIgnoreCase), context);
            Check(expect, "attempts", policy.Attempts, context);
            Check(expect, "delay", action.Delay, context);
            Check(expect, "target", policy.Target, context);
            Check(expect, "applied", policy.Applied, context);
            if (expect.TryGetProperty("targetMin", out var min)) Assert.True(policy.Target >= min.GetDouble(), context);
            if (expect.TryGetProperty("targetMax", out var max)) Assert.True(policy.Target <= max.GetDouble(), context);
        }
        var reportPath = Environment.GetEnvironmentVariable("TELEPILOT_TRACE_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            lock (ReportGate)
            {
                Reports[name] = report;
                File.WriteAllText(reportPath, JsonSerializer.Serialize(Reports.Values.SelectMany(rows => rows)));
            }
        }
    }

    private static void Record(List<object> report, string name, int index, double now, string operation,
        RecoveryAction action, PlaybackRecoveryPolicy policy) => report.Add(new
        {
            @case = name, index, now, op = operation, kind = action.Kind.ToString().ToLowerInvariant(), delay = action.Delay,
            target = policy.Target, applied = policy.Applied, attempts = policy.Attempts, pending = policy.Pending, gaveUp = policy.GaveUp,
        });

    private static double Number(JsonElement item, string name, double fallback) => item.TryGetProperty(name, out var value) ? value.GetDouble() : fallback;
    private static bool Flag(JsonElement item, string name, bool fallback) => item.TryGetProperty(name, out var value) ? value.GetBoolean() : fallback;
    private static void Check(JsonElement expect, string key, double actual, string context)
    {
        if (expect.TryGetProperty(key, out var value)) Assert.True(Math.Abs(value.GetDouble() - actual) <= 1e-6, $"{context}; expected {key}={value}");
    }
}
