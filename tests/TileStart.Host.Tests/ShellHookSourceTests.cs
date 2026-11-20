using System.IO;

namespace TileStart.Host.Tests;

public sealed class ShellHookSourceTests
{
    [Fact]
    public void NativeStartBypassRemainsSignaledAcrossDuplicateTaskListMessages()
    {
        var source = ReadSource();

        Assert.Contains(
            "CreateEventW(nullptr, TRUE, FALSE, kNativeStartBypassEventName)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "WaitForSingleObject(g_native_start_bypass_event, 0) == WAIT_OBJECT_0",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StartButtonClickChecksTheTopmostHitWindowBeforeOpeningTileStart()
    {
        var source = ReadSource();

        Assert.Contains("WindowFromPoint(point)", source, StringComparison.Ordinal);
        Assert.Contains("GetAncestor(hit_window, GA_ROOT) == taskbar", source, StringComparison.Ordinal);
        Assert.Contains("IsStartButtonHit(g_start_button, mouse->pt) && RequestHostOpen()", source,
            StringComparison.Ordinal);
    }

    private static string ReadSource() => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "TestData", "NativeSource", "ShellHook.cpp"));
}
