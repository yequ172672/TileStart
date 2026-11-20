using System.IO;
using System.Xml.Linq;
using TileStart.Host;

namespace TileStart.Host.Tests;

public sealed class StartWindowSizingTests
{
    [Fact]
    public void AllAppsOnlyWidthDoesNotReserveTheTilePaneInset()
    {
        Assert.Equal(
            Win10VisualMetrics.CollapsedNavigationWidth + Win10VisualMetrics.AllAppsWidth,
            StartWindowSizing.WidthForColumns(0));
    }

    [Fact]
    public void WidthTargetsExposeTheRequestedWorkspaceWidth()
    {
        for (var columns = 0; columns <= 3; columns++)
        {
            var viewportWidth = StartWindowSizing.WidthForColumns(columns)
                                - Win10VisualMetrics.CollapsedNavigationWidth
                                - Win10VisualMetrics.AllAppsWidth;

            var workspaceColumns = columns * TileWorkspaceMetrics.LegacyGroupWidthUnits;
            if (workspaceColumns == 0)
            {
                Assert.Equal(0, viewportWidth);
                continue;
            }

            viewportWidth -= Win10VisualMetrics.TileScrollViewerLeftMargin;

            Assert.Equal(workspaceColumns, Win10GroupWrapPanel.ColumnsForWidth(viewportWidth));
            Assert.True(viewportWidth >= Win10GroupWrapPanel.RequiredWidth(workspaceColumns));
        }
    }

    [Theory]
    [InlineData(1801, 2560, 1801)]
    [InlineData(2560, 2560, 2560)]
    [InlineData(4000, 2560, 2560)]
    [InlineData(1700, 1920 / 1.75, 1920 / 1.75)]
    [InlineData(400, 2560, 770)]
    [InlineData(1700, 640, 640)]
    public void DraggedWidthUsesTheMonitorAndDoesNotSnapToColumns(double requested, double available, double expected)
    {
        Assert.Equal(expected, StartWindowSizing.ClampWidth(requested, 770, available));
    }

    [Fact]
    public void ListWidthLeavesRoomForTheMinimumTileWorkspace()
    {
        var widthWithList = StartWindowSizing.WidthForColumns(1, 360);

        Assert.Equal(100, widthWithList - StartWindowSizing.WidthForColumns(1));
        Assert.Equal(360, StartWindowSizing.AppListWidthLimit(widthWithList, 1));
        Assert.Equal(360, StartWindowSizing.ClampAppListWidth(480, 360));
        Assert.Equal(StartWindowSizing.MaximumAppListWidth, StartWindowSizing.AppListWidthLimit(2560, 1));
    }

    [Fact]
    public void TileGroupsStretchFromTheResizableApplicationList()
    {
        var root = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestData", "Xaml", "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var names = root.Descendants().Where(element => element.Attribute(xaml + "Name") is not null);
        var groups = Assert.Single(names, element => (string?)element.Attribute(xaml + "Name") == "TileGroupsControl");
        var splitter = Assert.Single(names, element => (string?)element.Attribute(xaml + "Name") == "AppListSplitter");

        Assert.Equal("Stretch", (string?)groups.Attribute("HorizontalAlignment"));
        Assert.Equal("CurrentAndNext", (string?)splitter.Attribute("ResizeBehavior"));
        Assert.Equal("1", (string?)splitter.Attribute("Grid.Column"));
    }

    [Theory]
    [InlineData(2555, 1387, true)]
    [InlineData(1800, 1392, false)]
    [InlineData(2560, 1200, false)]
    public void DragFillsTheScreenOnlyWhenBothEdgesReachTheWorkArea(double width, double height, bool expected)
    {
        Assert.Equal(expected, StartWindowSizing.FillsArea(width, height, 2560, 1392));
    }

    [Theory]
    [InlineData(300, 480, 900)]
    [InlineData(700, 700, 900)]
    [InlineData(1200, 900, 900)]
    [InlineData(700, 400, 400)]
    public void HeightRemainsContinuousWithinTheWorkArea(
        double requestedHeight,
        double expectedHeight,
        double availableHeight)
    {
        Assert.Equal(expectedHeight, StartWindowSizing.ClampHeight(requestedHeight, 480, availableHeight));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(12, 1)]
    public void MinimumWidthDependsOnWhetherTilesExist(int tileCount, int expectedColumns)
    {
        Assert.Equal(expectedColumns, StartWindowSizing.MinimumColumnsForTileCount(tileCount));
    }

    [Theory]
    [InlineData(0, 8, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(1, 4, 1)]
    [InlineData(1, 5, 2)]
    [InlineData(8, 8, 2)]
    public void MinimumWidthIncludesTheWidestVisibleGroup(
        int tileCount,
        int widestGroupWidthUnits,
        int expectedWorkspaceColumns)
    {
        Assert.Equal(
            expectedWorkspaceColumns,
            StartWindowSizing.MinimumColumnsForTileLayout(tileCount, widestGroupWidthUnits));
    }

}
