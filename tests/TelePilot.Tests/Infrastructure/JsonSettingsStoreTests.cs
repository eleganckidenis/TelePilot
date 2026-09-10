using Microsoft.Extensions.Logging.Abstractions;
using TelePilot.Infrastructure;

namespace TelePilot.Tests.Infrastructure;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsSettingsAndLeavesNoTemporaryFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TelePilot.Tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "settings.json");
        try
        {
            using var store = new JsonSettingsStore<TestSettings>(
                file,
                static () => new TestSettings("domyślne", 25),
                NullLogger<JsonSettingsStore<TestSettings>>.Instance);

            await store.SaveAsync(new TestSettings("Łódź", 77));
            var loaded = await store.LoadAsync();

            Assert.Equal(new TestSettings("Łódź", 77), loaded);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Load_WithMalformedJson_ReturnsDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TelePilot.Tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(file, "{not json");
        try
        {
            using var store = new JsonSettingsStore<TestSettings>(
                file,
                static () => new TestSettings("domyślne", 25),
                NullLogger<JsonSettingsStore<TestSettings>>.Instance);

            Assert.Equal(new TestSettings("domyślne", 25), await store.LoadAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public sealed record TestSettings(string Name, int Volume);
}
