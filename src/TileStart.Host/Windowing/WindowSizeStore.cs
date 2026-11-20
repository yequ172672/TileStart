using System.IO;
using System.Text.Json;

namespace TileStart.Host.Windowing;

public readonly record struct WindowSizePreference(double Width, double Height, double AppListWidth, bool IsFullScreen);

public static class WindowSizeStore
{
    internal const int CurrentFormatVersion = 3;
    private const int ColumnFormatVersion = 2;

    private static readonly string DirectoryPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TileStart");

    private static readonly string FilePath = Path.Combine(DirectoryPath, "window.json");

    public static WindowSizePreference? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            return Deserialize(File.ReadAllText(FilePath));
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static WindowSizePreference? Deserialize(string json)
    {
        var size = JsonSerializer.Deserialize<SavedSize>(json);
        if (size is null || !double.IsFinite(size.Height) || size.Height <= 0)
        {
            return null;
        }

        if (size.Version >= CurrentFormatVersion)
        {
            return double.IsFinite(size.Width) && size.Width > 0
                ? new WindowSizePreference(size.Width, size.Height,
                    StartWindowSizing.ClampAppListWidth(size.AppListWidth, StartWindowSizing.MaximumAppListWidth),
                    size.IsFullScreen)
                : null;
        }

        if (size.Version < ColumnFormatVersion && (!double.IsFinite(size.Width) || size.Width <= 0))
        {
            return null;
        }

        var columns = size.Version == ColumnFormatVersion
            ? size.WorkspaceColumns
            : StartWindowSizing.LegacyColumnsForWidth(MigrateWidth(size.Width, size.Version));
        return columns is < StartWindowSizing.MinimumGroupColumns or > StartWindowSizing.LegacyMaximumGroupColumns
            ? null
            : new WindowSizePreference(StartWindowSizing.WidthForColumns(columns), size.Height,
                Win10VisualMetrics.AllAppsWidth, false);
    }

    internal static string Serialize(WindowSizePreference size) => JsonSerializer.Serialize(new SavedSize
    {
        Width = size.Width,
        Height = size.Height,
        AppListWidth = size.AppListWidth,
        IsFullScreen = size.IsFullScreen,
        // Older builds still read the nearest column count and height when rolling back.
        WorkspaceColumns = StartWindowSizing.LegacyColumnsForWidth(size.Width),
        Version = CurrentFormatVersion,
    });

    public static void Save(WindowSizePreference size)
    {
        if (!double.IsFinite(size.Width) || size.Width <= 0 || !double.IsFinite(size.Height) || size.Height <= 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, Serialize(size));
            File.Move(temporary, FilePath, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal static double MigrateWidth(double width, int version) =>
        version < 1
            ? width + Win10VisualMetrics.TileScrollBarLayoutWidth
            : width;

    private sealed class SavedSize
    {
        public double Width { get; init; }
        public double Height { get; init; }
        public double AppListWidth { get; init; }
        public bool IsFullScreen { get; init; }
        public int WorkspaceColumns { get; init; }
        public int Version { get; init; }
    }
}
