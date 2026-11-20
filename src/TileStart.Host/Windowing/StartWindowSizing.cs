using TileStart.Host.Tiles.Layout;

namespace TileStart.Host.Windowing;

public static class StartWindowSizing
{
    // Win10 permits the tile pane to be completely collapsed, leaving only
    // the navigation rail and the all-apps list visible.
    public const int MinimumGroupColumns = 0;
    internal const int LegacyMaximumGroupColumns = 3;
    internal const double MinimumAppListWidth = 180;
    internal const double MaximumAppListWidth = 560;
    private const double FullScreenDragTolerance = 8;

    public static int MinimumColumnsForTileCount(int tileCount) => tileCount > 0 ? 1 : 0;

    public static int MinimumColumnsForTileLayout(int tileCount, int widestGroupWidthUnits)
    {
        if (tileCount <= 0)
        {
            return MinimumGroupColumns;
        }

        var workspaceColumns = (Math.Max(1, widestGroupWidthUnits)
                                   + TileWorkspaceMetrics.LegacyGroupWidthUnits - 1)
                               / TileWorkspaceMetrics.LegacyGroupWidthUnits;
        return workspaceColumns;
    }

    public static double WidthForColumns(int columns, double appListWidth = Win10VisualMetrics.AllAppsWidth)
    {
        return Win10VisualMetrics.CollapsedNavigationWidth + appListWidth + TileWorkspaceWidth(columns);
    }

    private static double TileWorkspaceWidth(int columns) => columns <= 0
        ? 0
        : Win10VisualMetrics.TileScrollViewerLeftMargin
          + Win10GroupWrapPanel.RequiredWidth(columns * TileWorkspaceMetrics.LegacyGroupWidthUnits)
          + Win10VisualMetrics.TileScrollBarLayoutWidth;

    internal static int LegacyColumnsForWidth(double width) =>
        Enumerable.Range(MinimumGroupColumns, LegacyMaximumGroupColumns + 1)
            .OrderBy(columns => Math.Abs(WidthForColumns(columns) - width))
            .First();

    internal static double AppListWidthLimit(double windowWidth, int minimumColumns) =>
        Math.Clamp(windowWidth - Win10VisualMetrics.CollapsedNavigationWidth - TileWorkspaceWidth(minimumColumns),
            Math.Min(MinimumAppListWidth, Math.Max(0, windowWidth - Win10VisualMetrics.CollapsedNavigationWidth)),
            MaximumAppListWidth);

    internal static double ClampAppListWidth(double requestedWidth, double limit) =>
        Math.Clamp(double.IsFinite(requestedWidth) && requestedWidth > 0 ? requestedWidth : Win10VisualMetrics.AllAppsWidth,
            Math.Min(MinimumAppListWidth, limit), limit);

    internal static bool FillsArea(double width, double height, double areaWidth, double areaHeight) =>
        areaWidth > 0 && areaHeight > 0
        && width >= areaWidth - FullScreenDragTolerance
        && height >= areaHeight - FullScreenDragTolerance;

    public static double ClampWidth(double requestedWidth, double minimumWidth, double availableWidth) =>
        ClampDimension(requestedWidth, minimumWidth, availableWidth);

    public static double ClampHeight(double requestedHeight, double minimumHeight, double availableHeight) =>
        ClampDimension(requestedHeight, minimumHeight, availableHeight);

    private static double ClampDimension(double requested, double minimum, double available)
    {
        var validRequest = double.IsFinite(requested) ? requested : minimum;
        if (!double.IsFinite(available) || available <= 0)
        {
            return Math.Max(minimum, validRequest);
        }

        return Math.Clamp(validRequest, Math.Min(minimum, available), available);
    }
}
