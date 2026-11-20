using System.Diagnostics;
using System.IO;

namespace TileStart.Host.Updates;

internal static class UpdateInstallerLauncher
{
    private const string LaunchScript = """
        $ErrorActionPreference = 'Stop';
        $hostProcess = Get-Process -Id ([int]$env:TILESTART_UPDATE_PID) -ErrorAction SilentlyContinue;
        if ($hostProcess) { $hostProcess.WaitForExit(); }
        $directory = Split-Path -LiteralPath $env:TILESTART_UPDATE_HOST;
        try {
            $file = [IO.File]::OpenRead($env:TILESTART_UPDATE_PATH);
            $sha256 = [Security.Cryptography.SHA256]::Create();
            try { $hash = [BitConverter]::ToString($sha256.ComputeHash($file)).Replace('-', ''); }
            finally { $sha256.Dispose(); $file.Dispose(); }
            if ($hash -ne $env:TILESTART_UPDATE_SHA256) { throw 'Installer SHA-256 mismatch.'; }
            $arguments = @('/SP-', '/SILENT', '/NORESTART', '/RESTARTEXITCODE=9', '/NOCLOSEAPPLICATIONS',
                           ('/DIR="' + $directory + '"'), ('/TILESTARTUPDATE=' + $env:TILESTART_UPDATE_MODE));
            if ($env:TILESTART_UPDATE_MODE -eq 'portable') { $arguments += '/CURRENTUSER'; }
            $installerProcess = Start-Process -FilePath $env:TILESTART_UPDATE_PATH -ArgumentList $arguments -Wait -PassThru;
            if ($installerProcess.ExitCode -ne 0) { throw "Update installer returned $($installerProcess.ExitCode)."; }
            Remove-Item -LiteralPath $env:TILESTART_UPDATE_PATH -Force -ErrorAction SilentlyContinue;
            $updateDirectory = Split-Path -LiteralPath $env:TILESTART_UPDATE_PATH;
            if (-not (Get-ChildItem -LiteralPath $updateDirectory -Force)) {
                Remove-Item -LiteralPath $updateDirectory -Force -ErrorAction SilentlyContinue;
            }
        } catch {
            Add-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'TileStart\UpdateInstall.log') -Value $_.Exception.Message;
        } finally {
            Start-Process -FilePath $env:TILESTART_UPDATE_HOST -WorkingDirectory $directory;
        }
        """;

    public static void LaunchAfterHostExit(DownloadedUpdate update, string hostPath, int hostProcessId, bool portable)
    {
        _ = Process.Start(CreateStartInfo(update, hostPath, hostProcessId, portable))
            ?? throw new InvalidOperationException("无法启动更新安装助手。");
    }

    internal static ProcessStartInfo CreateStartInfo(DownloadedUpdate update, string hostPath, int hostProcessId, bool portable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostPath);
        if (hostProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hostProcessId));
        }

        var fullInstallerPath = Path.GetFullPath(update.Path);
        var managedUpdateRoot = Path.Combine(Path.GetTempPath(), "TileStart", "updates") + Path.DirectorySeparatorChar;
        if (!fullInstallerPath.StartsWith(managedUpdateRoot, StringComparison.OrdinalIgnoreCase)
            || !fullInstallerPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("更新安装器必须位于 TileStart 临时更新目录中。", nameof(update));
        }

        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var startInfo = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(fullInstallerPath) ?? Path.GetTempPath(),
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(LaunchScript);
        // 路径不拼进 PowerShell 命令，避免空格和特殊字符改变脚本含义。
        startInfo.Environment["TILESTART_UPDATE_PATH"] = fullInstallerPath;
        startInfo.Environment["TILESTART_UPDATE_HOST"] = Path.GetFullPath(hostPath);
        startInfo.Environment["TILESTART_UPDATE_PID"] = hostProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["TILESTART_UPDATE_SHA256"] = update.Sha256;
        startInfo.Environment["TILESTART_UPDATE_MODE"] = portable ? "portable" : "installed";
        return startInfo;
    }
}
