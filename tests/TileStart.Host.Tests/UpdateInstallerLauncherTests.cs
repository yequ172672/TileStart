using System.Diagnostics;
using System.IO;
using TileStart.Host.Updates;

namespace TileStart.Host.Tests;

public sealed class UpdateInstallerLauncherTests
{
    [Fact]
    public void WaitsForHostAndCleansOnlySuccessfulManagedInstaller()
    {
        var installerPath = Path.Combine(Path.GetTempPath(), "TileStart", "updates", "v0.1.19", "attempt", "setup.exe");

        var hostPath = Path.Combine(Path.GetTempPath(), "portable dir", "TileStart.Host.exe");
        var update = new DownloadedUpdate(UpdatePackageKind.Installer, installerPath, new Version(0, 1, 19), new string('a', 64));
        var startInfo = UpdateInstallerLauncher.CreateStartInfo(update, hostPath, 1234, portable: true);

        Assert.EndsWith("powershell.exe", startInfo.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
        Assert.Equal(Path.GetFullPath(installerPath), startInfo.Environment["TILESTART_UPDATE_PATH"]);
        Assert.Equal("1234", startInfo.Environment["TILESTART_UPDATE_PID"]);
        Assert.Equal(hostPath, startInfo.Environment["TILESTART_UPDATE_HOST"]);
        Assert.Equal("portable", startInfo.Environment["TILESTART_UPDATE_MODE"]);
        Assert.Equal("0", startInfo.Environment["TILESTART_UPDATE_UNATTENDED"]);
        Assert.Equal(update.Sha256, startInfo.Environment["TILESTART_UPDATE_SHA256"]);
        Assert.Contains(startInfo.ArgumentList, argument => argument.Contains("WaitForExit()", StringComparison.Ordinal));
        Assert.Contains(startInfo.ArgumentList, argument => argument.Contains("-Wait -PassThru", StringComparison.Ordinal));
        Assert.Contains(startInfo.ArgumentList, argument => argument.Contains("ExitCode -ne 0", StringComparison.Ordinal));
        Assert.Contains(startInfo.ArgumentList, argument => argument.Contains("Remove-Item", StringComparison.Ordinal));
        Assert.Contains(startInfo.ArgumentList, argument =>
            argument.Contains("Start-Process -FilePath $env:TILESTART_UPDATE_HOST", StringComparison.Ordinal));
        Assert.DoesNotContain(startInfo.ArgumentList,
            (string argument) => argument.Contains(installerPath, StringComparison.Ordinal));
    }

    [Fact]
    public void UnattendedLaunchSeparatelyRequestsAQuietInstaller()
    {
        var installer = Path.Combine(Path.GetTempPath(), "TileStart", "updates", "v0.2.8", "attempt", "setup.exe");
        var update = new DownloadedUpdate(UpdatePackageKind.PatchInstaller, installer, new Version(0, 2, 8), new string('a', 64));
        var start = UpdateInstallerLauncher.CreateStartInfo(update, @"C:\TileStart\TileStart.Host.exe", 1234,
            portable: true, unattended: true);

        Assert.Equal("1", start.Environment["TILESTART_UPDATE_UNATTENDED"]);
        Assert.Contains(start.ArgumentList, argument => argument.Contains("/VERYSILENT", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsInstallerOutsideManagedUpdateDirectory()
    {
        var installerPath = Path.Combine(Path.GetTempPath(), "unrelated", "setup.exe");

        var update = new DownloadedUpdate(UpdatePackageKind.Installer, installerPath, new Version(0, 1, 19), new string('a', 64));
        Assert.Throws<ArgumentException>(() => UpdateInstallerLauncher.CreateStartInfo(update, @"C:\TileStart\TileStart.Host.exe", 1234, portable: false));
    }
}
