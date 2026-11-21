using System.IO;
using System.Security.Principal;
using System.Windows.Threading;
using TileStart.Host.Utilities;

namespace TileStart.Host.Updates;

internal sealed class IdleUpdateCoordinator : IDisposable
{
    private static readonly string PreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TileStart", "automatic-update.json");

    private readonly GitHubUpdateService _service;
    private readonly DispatcherTimer _timer;
    private readonly Func<bool> _isTileStartBusy;
    private readonly Action<DownloadedUpdate> _install;
    private readonly string _hostPath;
    private readonly bool _installedCopy;
    private AutomaticUpdatePreferences _preferences = AutomaticUpdatePreferences.Load(PreferencesPath);
    private UpdateIdleMonitor? _monitor;
    private CancellationTokenSource? _download;
    private DownloadedUpdate? _staged;
    private DateTimeOffset _nextCheck;

    internal IdleUpdateCoordinator(GitHubUpdateService service, Dispatcher dispatcher, string hostPath,
        Func<bool> isTileStartBusy, Action<DownloadedUpdate> install)
    {
        _service = service;
        _hostPath = hostPath;
        _isTileStartBusy = isTileStartBusy;
        _install = install;
        _installedCopy = GitHubUpdateService.IsInstalledCopy(hostPath);
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = UpdateIdleMonitor.PollInterval };
        _timer.Tick += OnTick;

        if (_preferences.Enabled)
        {
            try { StartMonitoring(); }
            catch (Exception exception) { DiagnosticLog.Write($"Automatic update monitoring unavailable: {exception.Message}"); }
        }
    }

    internal bool Enabled => _preferences.Enabled;
    internal bool IsDownloading => _download is not null;
    internal Version? StagedVersion => _staged?.Version;

    internal void SetEnabled(bool enabled)
    {
        if (enabled == Enabled && (!enabled || _monitor is not null))
        {
            return;
        }

        if (enabled)
        {
            StartMonitoring();
        }

        try
        {
            SavePreferences(_preferences with { Enabled = enabled });
        }
        catch
        {
            if (enabled) { StopMonitoring(); }
            throw;
        }

        if (!enabled)
        {
            StopMonitoring();
        }
    }

    internal DownloadedUpdate? TakeStaged(Version version)
    {
        if (_staged is not { } staged || staged.Version != version || !File.Exists(staged.Path))
        {
            return null;
        }

        _staged = null;
        return staged;
    }

    private void StartMonitoring()
    {
        _monitor ??= new UpdateIdleMonitor();
        _nextCheck = default;
        _timer.Start();
    }

    private void StopMonitoring()
    {
        _timer.Stop();
        _download?.Cancel();
        _monitor?.Dispose();
        _monitor = null;
        DiscardStaged();
    }

    private bool IsSafeToUpdate() => _monitor?.HasBeenIdle() == true
        && !_isTileStartBusy() && UpdateIdleMonitor.CanInterruptDesktop();

    private async void OnTick(object? sender, EventArgs e)
    {
        try
        {
            if (!IsSafeToUpdate())
            {
                _download?.Cancel();
                return;
            }

            if (_download is not null) { return; }
            if (TryInstallStaged() || DateTimeOffset.UtcNow < _nextCheck) { return; }

            using var download = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            _download = download;
            var token = download.Token;
            _nextCheck = DateTimeOffset.UtcNow.AddHours(6);
            try
            {
                var release = await _service.GetLatestReleaseAsync(token);
                if (!IsSafeToUpdate())
                {
                    _nextCheck = DateTimeOffset.UtcNow.AddMinutes(15);
                    return;
                }

                if (!GitHubUpdateService.IsNewer(GitHubUpdateService.CurrentVersion, release.Version)
                    || _staged is { } current && current.Version >= release.Version && File.Exists(current.Path)
                    || WasAttemptedRecently(release.Version))
                {
                    return;
                }

                var update = await _service.DownloadAsync(release, GitHubUpdateService.CurrentVersion, _hostPath,
                    cancellationToken: token);
                if (token.IsCancellationRequested || _monitor is null)
                {
                    GitHubUpdateService.DeleteDownload(update.Path);
                    token.ThrowIfCancellationRequested();
                    return;
                }

                DiscardStaged();
                _staged = update;
                DiagnosticLog.Write($"Automatic update verified and staged: {update.Version}, {update.Kind}.");
                if (IsSafeToUpdate()) { TryInstallStaged(); }
            }
            finally
            {
                _download = null;
            }
        }
        catch (OperationCanceledException)
        {
            _nextCheck = DateTimeOffset.UtcNow.AddMinutes(15);
        }
        catch (Exception exception)
        {
            _nextCheck = DateTimeOffset.UtcNow.AddHours(1);
            DiagnosticLog.Write($"Automatic update deferred: {exception.Message}");
        }
    }

    private bool TryInstallStaged()
    {
        if (_staged is not { } update)
        {
            return false;
        }

        if (!File.Exists(update.Path))
        {
            _staged = null;
            _nextCheck = default;
            return false;
        }

        if (update.Kind is not (UpdatePackageKind.Installer or UpdatePackageKind.PatchInstaller)
            || WasAttemptedRecently(update.Version) || (_installedCopy && !IsElevated()) || !CanWriteInstallDirectory())
        {
            return false;
        }

        SavePreferences(_preferences with { LastAttemptVersion = update.Version.ToString(3), LastAttemptUtc = DateTimeOffset.UtcNow });
        _staged = null; // The installation helper owns the verified download after handoff.
        try { _install(update); }
        catch { GitHubUpdateService.DeleteDownload(update.Path); throw; }
        return true;
    }

    private bool WasAttemptedRecently(Version version) => _preferences.WasAttemptedRecently(version, DateTimeOffset.UtcNow);

    private bool CanWriteInstallDirectory()
    {
        var directory = Path.GetDirectoryName(_hostPath)!;
        try
        {
            using var probe = File.Create(Path.Combine(directory, $".tilestart-{Guid.NewGuid():N}"), 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void SavePreferences(AutomaticUpdatePreferences preferences)
    {
        preferences.Save(PreferencesPath);
        _preferences = preferences;
    }

    private void DiscardStaged()
    {
        if (_staged is not { } update) { return; }
        _staged = null;
        GitHubUpdateService.DeleteDownload(update.Path);
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public void Dispose()
    {
        StopMonitoring();
        _timer.Tick -= OnTick;
    }
}
