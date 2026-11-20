using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TileStart.Host.Tiles.Layout;
using TileStart.Host.Tiles.Models;
using TileStart.Host.Utilities;

namespace TileStart.Host.Applications;

public readonly record struct NativeStartImportResult(int Added, int Existing, int Unavailable);

internal static class NativeStartPins
{
    internal const string GroupName = "Windows 11 固定项";

    public static async Task<string> ExportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exportPath = Path.Combine(Path.GetTempPath(), $"TileStart-pins-{Guid.NewGuid():N}.json");
        try
        {
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            var startInfo = new ProcessStartInfo(powershell)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("Export-StartLayout -Path $env:TILESTART_PINS_EXPORT -ErrorAction Stop");
            startInfo.Environment["TILESTART_PINS_EXPORT"] = exportPath;

            using var export = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 Windows 固定项导出。");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var errorOutput = export.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                await export.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                var error = await errorOutput.ConfigureAwait(false);
                if (export.ExitCode != 0)
                {
                    DiagnosticLog.Write($"Native Start pins export failed: exit={export.ExitCode}, {error.Trim()}");
                    throw new InvalidOperationException("Windows 无法导出当前账户的固定项。");
                }
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!export.HasExited)
                    {
                        export.Kill();
                        await export.WaitForExitAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    DiagnosticLog.Write($"Unable to stop Native Start pins export: {exception.Message}");
                }

                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("读取 Windows 固定项超时。");
            }

            return await File.ReadAllTextAsync(exportPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(exportPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                DiagnosticLog.Write($"Unable to clean up Native Start pins export: {exception.Message}");
            }
        }
    }

    public static (List<TileItem> Tiles, int Unavailable) Resolve(string json, IReadOnlyList<AppEntry> apps)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("pinnedList", out var pins)
            || pins.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Windows 返回的开始菜单布局没有固定列表。");
        }

        var appsByTarget = apps.ToLookup(app => app.LaunchTarget, StringComparer.OrdinalIgnoreCase);
        var appsById = apps.Where(app => !string.IsNullOrWhiteSpace(app.AppUserModelId))
            .ToLookup(app => app.AppUserModelId, StringComparer.OrdinalIgnoreCase);
        var tiles = new List<TileItem>();
        var unavailable = 0;
        foreach (var pin in pins.EnumerateArray())
        {
            var link = ReadString(pin, "desktopAppLink");
            var appId = ReadString(pin, "packagedAppId") ?? ReadString(pin, "desktopAppId");
            if (link is not null)
            {
                var path = Environment.ExpandEnvironmentVariables(link);
                if (Path.IsPathFullyQualified(path) && File.Exists(path))
                {
                    var app = appsByTarget[path].FirstOrDefault();
                    tiles.Add(new TileItem
                    {
                        Name = app?.Name ?? Path.GetFileNameWithoutExtension(path),
                        LaunchTarget = path,
                        TargetType = Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase)
                            ? TileTargetType.Url
                            : TileTargetType.Application,
                        Size = TileSize.Medium,
                    });
                    continue;
                }
            }
            else if (appId is not null && appsById[appId].FirstOrDefault() is { } app)
            {
                tiles.Add(new TileItem
                {
                    Name = app.Name,
                    LaunchTarget = app.LaunchTarget,
                    TargetType = TileTargetType.Application,
                    Size = TileSize.Medium,
                });
                continue;
            }

            unavailable++;
        }

        return (tiles, unavailable);
    }

    public static TileGroup BuildGroup(TileLayout layout, IReadOnlyList<TileItem> pins)
    {
        using var resolver = LaunchTargetIdentity.CreateResolver();
        var existing = layout.EnumerateTiles()
            .Where(tile => !string.IsNullOrWhiteSpace(tile.LaunchTarget))
            .Select(tile => resolver.GetKey(tile.LaunchTarget))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var group = new TileGroup
        {
            Name = GroupName,
            GroupColumn = 0,
            GroupRow = layout.Groups.Select(item => item.GroupRow).DefaultIfEmpty(-1).Max() + 1,
        };
        foreach (var tile in pins)
        {
            if (existing.Add(resolver.GetKey(tile.LaunchTarget))
                && Win10GroupLayout.TryFindFirstAvailable(group, tile, out var cell))
            {
                Win10GroupLayout.AddToFreeCell(group, tile, cell.Column, cell.Row);
            }
        }

        return group;
    }

    private static string? ReadString(JsonElement pin, string name)
    {
        if (pin.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in pin.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString()?.Trim() is { Length: > 0 } value)
                {
                    return value;
                }
            }
        }

        return null;
    }
}
