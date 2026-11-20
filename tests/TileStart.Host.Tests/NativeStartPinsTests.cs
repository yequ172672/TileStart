using System.IO;
using System.Text.Json;

namespace TileStart.Host.Tests;

public sealed class NativeStartPinsTests
{
    [Fact]
    public void ResolveKeepsExportOrderAndSkipsUnsupportedOrMissingPins()
    {
        const string packagedId = "Example.Package_123!App";
        var packaged = AppEntry.Application("Packaged app", $@"shell:AppsFolder\{packagedId}",
            DateTime.MinValue, appUserModelId: packagedId);
        var desktop = AppEntry.Application("Desktop app", @"C:\Apps\Desktop.exe",
            DateTime.MinValue, appUserModelId: "Example.Desktop");
        var shortcut = Path.Combine(Path.GetTempPath(), $"TileStart-{Guid.NewGuid():N}.lnk");
        File.Create(shortcut).Dispose();
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                pinnedList = new object[]
                {
                    new { desktopAppLink = shortcut },
                    new { packagedAppId = packagedId.ToUpperInvariant() },
                    new { desktopAppID = "Example.Desktop" },
                    new { secondaryTile = new { displayName = "Web pin" } },
                    new { desktopAppLink = shortcut + ".missing" },
                    new { packagedAppId = "Missing.Package!App" },
                },
            });

            var result = NativeStartPins.Resolve(json, [packaged, desktop]);

            Assert.Equal(3, result.Unavailable);
            Assert.Equal(new[] { shortcut, packaged.LaunchTarget, desktop.LaunchTarget },
                result.Tiles.Select(tile => tile.LaunchTarget));
            Assert.All(result.Tiles, tile => Assert.Equal(TileSize.Medium, tile.Size));
        }
        finally
        {
            File.Delete(shortcut);
        }
    }

    [Fact]
    public void BuildGroupDeduplicatesAcrossExistingTilesAndRepeatedImports()
    {
        var first = new TileItem { LaunchTarget = @"C:\Apps\First.exe" };
        var second = new TileItem { LaunchTarget = @"C:\Apps\Second.exe" };
        var existing = new TileGroup { GroupColumn = 2, GroupRow = 3, Tiles = [first] };
        var layout = new TileLayout { Groups = [existing] };
        TileItem[] pins =
        [
            new() { LaunchTarget = first.LaunchTarget.ToUpperInvariant() },
            second,
            new() { LaunchTarget = second.LaunchTarget.ToUpperInvariant() },
        ];

        var result = NativeStartPins.BuildGroup(layout, pins);

        Assert.Same(second, Assert.Single(result.Tiles));
        Assert.Equal(NativeStartPins.GroupName, result.Name);
        Assert.Equal(4, result.GroupRow);
        Assert.Same(existing, Assert.Single(layout.Groups));
        Assert.Equal((2, 3), (existing.GroupColumn, existing.GroupRow));
        layout.Groups.Add(result);
        Assert.Empty(NativeStartPins.BuildGroup(layout, pins).Tiles);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"pinnedList\":null}")]
    [InlineData("[]")]
    public void ResolveRejectsAnExportWithoutAPinnedList(string json)
    {
        Assert.Throws<InvalidDataException>(() => NativeStartPins.Resolve(json, []));
    }
}
