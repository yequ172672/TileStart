using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using TileStart.Host.Updates;
using TileStart.Host.Windowing;

namespace TileStart.Host.About;

public partial class AboutWindow : Window
{
    internal static readonly Uri ProjectUri = new(GitHubUpdateService.RepositoryUrl);
    private readonly Func<Window, Task> _checkForUpdates;

    public AboutWindow(Func<Window, Task> checkForUpdates)
    {
        _checkForUpdates = checkForUpdates;
        InitializeComponent();
        VersionText.Text = $"版本 {GitHubUpdateService.CurrentVersion.ToString(3)}";
        var executablePath = Environment.ProcessPath;
        EditionText.Text = GitHubUpdateService.IsInstalledCopy(executablePath) ? "安装版" : "便携版";
        if (GitHubUpdateService.IsFrameworkDependentCopy(executablePath))
        {
            EditionText.Text += " · 使用系统 .NET";
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await _checkForUpdates(this);
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e) => OpenLink(ProjectUri);

    private void MaintainerLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        OpenLink(e.Uri);
    }

    private void OpenLink(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            TileStartMessageDialog.Show(
                this,
                "无法打开链接",
                exception.Message,
                TileStartMessageKind.Error);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
}
