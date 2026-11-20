using TileStart.Host;

namespace TileStart.Host.Tests;

public sealed class WindowSizeStoreTests
{
    [Fact]
    public void VersionTwoReadsTheOldColumnCountAndHeight()
    {
        var result = WindowSizeStore.Deserialize("""{"Width":0,"Height":1243,"WorkspaceColumns":3,"Version":2}""");

        Assert.Equal(new WindowSizePreference(StartWindowSizing.WidthForColumns(3), 1243, 260, false), result);
    }

    [Fact]
    public void FullScreenRoundTripsWithoutLosingTheNormalWindowAndListWidths()
    {
        var size = new WindowSizePreference(1839, 960, 381, true);

        Assert.Equal(size, WindowSizeStore.Deserialize(WindowSizeStore.Serialize(size)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LegacyWidthAddsOneScrollBarSafetyGapWithoutChangingTheRequestedColumnCount(int columns)
    {
        var fixedWindowWidth = Win10VisualMetrics.CollapsedNavigationWidth
                               + Win10VisualMetrics.AllAppsWidth
                               + Win10VisualMetrics.TileScrollViewerLeftMargin;
        var savedWindowWidth = fixedWindowWidth + Win10GroupWrapPanel.RequiredWidth(columns);

        var migratedWidth = WindowSizeStore.MigrateWidth(savedWindowWidth, version: 0);
        var tileViewportWidth = migratedWidth - fixedWindowWidth;
        var rightSafetyGap = tileViewportWidth - Win10GroupWrapPanel.RequiredWidth(columns);

        Assert.Equal(
            savedWindowWidth + Win10VisualMetrics.TileScrollBarLayoutWidth,
            migratedWidth,
            precision: 8);
        Assert.Equal(columns, Win10GroupWrapPanel.ColumnsForWidth(tileViewportWidth));
        Assert.Equal(Win10VisualMetrics.TileScrollBarLayoutWidth, rightSafetyGap, precision: 8);
        Assert.Equal(
            migratedWidth,
            WindowSizeStore.MigrateWidth(migratedWidth, WindowSizeStore.CurrentFormatVersion));
        Assert.Equal(migratedWidth, WindowSizeStore.MigrateWidth(migratedWidth, version: 1));
    }
}
