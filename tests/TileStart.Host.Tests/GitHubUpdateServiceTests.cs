using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using TileStart.Host.Updates;

namespace TileStart.Host.Tests;

public sealed class GitHubUpdateServiceTests
{
    private const string ReleaseJson = """
                                               {
                                                 "tag_name": "v0.2.0",
                                                 "html_url": "https://github.com/yequ172672/TileStart/releases/tag/v0.2.0",
                                                 "assets": [
                                                   {
                                                     "name": "TileStart-Setup-win-x64.exe",
                                                     "browser_download_url": "https://github.com/yequ172672/TileStart/releases/download/v0.2.0/TileStart-Setup-win-x64.exe"
                                                   },
                                                   {
                                                     "name": "TileStart-portable-win-x64.zip",
                                                     "browser_download_url": "https://github.com/yequ172672/TileStart/releases/download/v0.2.0/TileStart-portable-win-x64.zip"
                                                   },
                                                   {
                                                     "name": "SHA256SUMS.txt",
                                                     "browser_download_url": "https://github.com/yequ172672/TileStart/releases/download/v0.2.0/SHA256SUMS.txt"
                                                   }
                                                 ]
                                               }
                                               """;

    [Fact]
    public void ParsesReleaseAndSelectsPackageForInstallType()
    {
        var release = GitHubUpdateService.ParseRelease(ReleaseJson);

        Assert.Equal(new Version(0, 2, 0, 0), release.Version);
        Assert.Equal("https://github.com/yequ172672/TileStart/releases/tag/v0.2.0", release.ReleasePage.AbsoluteUri);
        Assert.Equal(UpdatePackageKind.Installer, GitHubUpdateService.SelectPackage(release, true).Kind);
        Assert.Equal(UpdatePackageKind.PortableArchive, GitHubUpdateService.SelectPackage(release, false).Kind);
    }

    [Theory]
    [InlineData("0.1.0", "0.1.1", true)]
    [InlineData("0.1.1", "0.1.1", false)]
    [InlineData("0.2.0", "0.1.9", false)]
    public void ComparesNormalizedVersions(string current, string available, bool expected)
    {
        Assert.Equal(expected,
            GitHubUpdateService.IsNewer(Version.Parse(current), Version.Parse(available)));
    }

    [Fact]
    public void ReadsExpectedHashForNamedAsset()
    {
        var expected = new string('a', 64);
        var manifest = $"{new string('b', 64)} *other.zip\n{expected} *TileStart-Setup-win-x64.exe\n";

        Assert.Equal(expected,
            GitHubUpdateService.ReadExpectedSha256(manifest, "TileStart-Setup-win-x64.exe"));
    }

    [Fact]
    public void RejectsReleaseWithoutRequiredAsset()
    {
        var json = ReleaseJson.Replace("TileStart-portable-win-x64.zip", "portable.zip", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => GitHubUpdateService.ParseRelease(json));
    }

    [Theory]
    [InlineData("https://example.com/TileStart-Setup-win-x64.exe")]
    [InlineData("https://github.com/Narylr350/TileStart/releases/download/v0.2.0/TileStart-Setup-win-x64.exe")]
    [InlineData("https://github.com/yequ172672/TileStart/releases/download/v0.2.1/TileStart-Setup-win-x64.exe")]
    [InlineData("https://github.com/yequ172672/TileStart/releases/download/v0.2.0/SHA256SUMS.txt")]
    public void RejectsAssetsOutsideTheExpectedForkReleaseAndFile(string uri)
    {
        var json = ReleaseJson.Replace(
            "https://github.com/yequ172672/TileStart/releases/download/v0.2.0/TileStart-Setup-win-x64.exe",
            uri,
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => GitHubUpdateService.ParseRelease(json));
    }

    [Fact]
    public void DetectsInstalledCopyFromInnoUninstaller()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tilestart-update-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var executable = Path.Combine(directory, "TileStart.Host.exe");
            File.WriteAllText(executable, string.Empty);
            Assert.False(GitHubUpdateService.IsInstalledCopy(executable));

            File.WriteAllText(Path.Combine(directory, "unins000.exe"), string.Empty);
            Assert.True(GitHubUpdateService.IsInstalledCopy(executable));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CreatesUniqueDownloadDirectoryForEveryAttempt()
    {
        var first = GitHubUpdateService.CreateDownloadDirectory(new Version(0, 2, 0));
        var second = GitHubUpdateService.CreateDownloadDirectory(new Version(0, 2, 0));

        Assert.NotEqual(first, second);
        Assert.Contains(Path.Combine("TileStart", "updates", "v0.2.0"), first, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, true, true, "patch", UpdatePackageKind.PatchInstaller)]
    [InlineData(false, true, true, "patch", UpdatePackageKind.PatchInstaller)]
    [InlineData(true, false, true, "patch", UpdatePackageKind.Installer)]
    [InlineData(false, true, false, "patch", UpdatePackageKind.Installer)]
    [InlineData(true, true, true, "corrupt patch", UpdatePackageKind.Installer)]
    [InlineData(true, true, true, "framework dependent", UpdatePackageKind.Installer)]
    [InlineData(false, true, true, "full only", UpdatePackageKind.Installer)]
    [InlineData(false, true, true, "legacy", UpdatePackageKind.PortableArchive)]
    public async Task DownloadsVerifiedPatchOrAutomaticallyFallsBackToFullPackage(
        bool installed, bool versionMatches, bool hashMatches, string releaseType, UpdatePackageKind expected)
    {
        const string patchName = "TileStart-Patch-win-x64.exe";
        const string manifestName = "TileStart-Update.txt";
        const string frameworkDependentName = "TileStart-Setup-no-runtime-win-x64.exe";
        const string downloadRoot = "https://github.com/yequ172672/TileStart/releases/download/v0.2.0/";
        var hasPatch = releaseType is "patch" or "corrupt patch" or "framework dependent";
        var release = GitHubUpdateService.ParseRelease(ReleaseJson) with
        {
            PatchInstaller = hasPatch ? new GitHubReleaseAsset(patchName, new Uri(downloadRoot + patchName)) : null,
            UpdateManifest = releaseType != "legacy" ? new GitHubReleaseAsset(manifestName, new Uri(downloadRoot + manifestName)) : null,
            FrameworkDependentInstaller = new GitHubReleaseAsset(frameworkDependentName, new Uri(downloadRoot + frameworkDependentName)),
        };
        var assets = new Dictionary<string, string>
        {
            [release.Installer.Name] = "complete installer",
            [frameworkDependentName] = "installer without bundled runtime",
            [release.PortableArchive.Name] = "portable archive",
            [patchName] = "small installer",
            [manifestName] = hasPatch
                ? $"TileStart.Update.v1\n0.1.9\n{Hash("baseline host")} *TileStart.Host.dll\n"
                : "TileStart.Update.v1\n-\n",
        };
        assets[release.Checksums.Name] = string.Join('\n', assets.Select(asset => $"{Hash(asset.Value)} *{asset.Key}"));
        if (releaseType == "corrupt patch")
        {
            assets[patchName] = "unexpected content";
        }

        using var client = new HttpClient(new ReleaseAssetHandler(assets));
        var directory = Directory.CreateTempSubdirectory("tilestart-update-baseline-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "TileStart.Host.dll"),
                hashMatches ? "baseline host" : "locally modified host");
            if (installed)
            {
                File.WriteAllText(Path.Combine(directory.FullName, "unins000.exe"), string.Empty);
            }

            if (releaseType == "framework dependent")
            {
                File.WriteAllText(Path.Combine(directory.FullName, "TileStart.Host.runtimeconfig.json"),
                    """{"runtimeOptions":{"frameworks":[{"name":"Microsoft.WindowsDesktop.App","version":"8.0.0"}]}}""");
            }

            var service = new GitHubUpdateService(client);
            var current = versionMatches ? new Version(0, 1, 9, 0) : new Version(0, 1, 8);
            var result = await service.DownloadAsync(release, current, Path.Combine(directory.FullName, "TileStart.Host.exe"));
            try
            {
                Assert.Equal(expected, result.Kind);
                if (expected == UpdatePackageKind.Installer)
                {
                    Assert.Equal(releaseType == "framework dependent" ? frameworkDependentName : release.Installer.Name,
                        Path.GetFileName(result.Path));
                }
                Assert.Equal(Hash(assets[Path.GetFileName(result.Path)]), result.Sha256);
            }
            finally
            {
                File.Delete(result.Path);
                Directory.Delete(Path.GetDirectoryName(result.Path)!);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("../TileStart.Host.dll")]
    [InlineData("bin/./TileStart.Host.dll")]
    [InlineData("C:/TileStart.Host.dll")]
    public void RejectsInvalidBaselinePaths(string path)
    {
        var manifest = $"TileStart.Update.v1\n0.1.9\n{Hash("base")} *TileStart.Host.dll\n{Hash("other")} *{path}\n";
        Assert.Throws<InvalidDataException>(() => UpdateReleaseManifest.Parse(manifest));
    }

    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private sealed class ReleaseAssetHandler(IReadOnlyDictionary<string, string> assets) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(assets[request.RequestUri!.Segments[^1]], Encoding.UTF8),
            });
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(50, 100, 50)]
    [InlineData(150, 100, 100)]
    public void CalculatesBoundedDownloadPercentage(long received, long total, double expected)
    {
        var progress = new UpdateProgressInfo(UpdateProgressStage.DownloadingPackage, received, total);

        Assert.Equal(expected, progress.Percentage);
    }

    [Fact]
    public void LeavesPercentageUnknownWithoutContentLength()
    {
        var progress = new UpdateProgressInfo(UpdateProgressStage.DownloadingPackage, 42, null);

        Assert.Null(progress.Percentage);
    }
}
