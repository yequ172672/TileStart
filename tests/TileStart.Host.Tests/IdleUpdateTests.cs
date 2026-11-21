using System.IO;
using TileStart.Host.Updates;

namespace TileStart.Host.Tests;

public sealed class IdleUpdateTests
{
    private const long Interval = 15_000;
    private const long Hour = 3_600_000;

    [Fact]
    public void AutomaticUpdatesAreOptInAndPersistedLocally()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "automatic-update.json");

        Assert.False(AutomaticUpdatePreferences.Load(path).Enabled);
        File.WriteAllText(path, "{}");
        Assert.False(AutomaticUpdatePreferences.Load(path).Enabled);
        (new AutomaticUpdatePreferences() with { Enabled = true }).Save(path);
        Assert.True(AutomaticUpdatePreferences.Load(path).Enabled);
        File.WriteAllText(path, "invalid JSON");
        Assert.False(AutomaticUpdatePreferences.Load(path).Enabled);
    }

    [Fact]
    public void RequiresOneHourOfContinuousIdleObservation()
    {
        var clock = new UpdateIdleClock(0);
        ObserveThrough(clock, Interval, Hour - Interval, expected: false);
        Assert.True(clock.Observe(Hour, inputObserved: false));
    }

    [Fact]
    public void InputAndObservationGapsRestartTheIdleWindow()
    {
        var clock = new UpdateIdleClock(0);
        ObserveThrough(clock, Interval, Hour - Interval, expected: false);
        Assert.False(clock.Observe(Hour, inputObserved: true));
        ObserveThrough(clock, Hour + Interval, 2 * Hour - Interval, expected: false);
        Assert.True(clock.Observe(2 * Hour, inputObserved: false));

        var afterSleep = 4 * Hour;
        Assert.False(clock.Observe(afterSleep, inputObserved: false));
        ObserveThrough(clock, afterSleep + Interval, afterSleep + Hour - Interval, expected: false);
        Assert.True(clock.Observe(afterSleep + Hour, inputObserved: false));
    }

    [Fact]
    public void BacksOffOnlyForTheVersionThatAlreadyAttemptedInstallation()
    {
        var now = new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);
        var update = new Version(0, 2, 8, 0);
        var preferences = new AutomaticUpdatePreferences(true, "0.2.8", now.AddHours(-1));

        Assert.True(preferences.WasAttemptedRecently(update, now));
        Assert.False(preferences.WasAttemptedRecently(new Version(0, 2, 9), now));
        Assert.False(preferences.WasAttemptedRecently(update, now.AddHours(23)));
    }

    private static void ObserveThrough(UpdateIdleClock clock, long start, long end, bool expected)
    {
        for (var now = start; now <= end; now += Interval)
        {
            Assert.Equal(expected, clock.Observe(now, inputObserved: false));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("tilestart-idle-settings-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
