using System.Diagnostics;
using FControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FControl.Pages;

public sealed partial class AboutPage : Page
{
    private readonly string _currentVersionText;
    private CancellationTokenSource? _downloadCts;

    public AboutPage()
    {
        InitializeComponent();
        _currentVersionText = UpdateService.GetCurrentVersionText();
        VersionText.Text = $"版本：{_currentVersionText}";

        if (UpdateService.IsAvailable)
        {
            UpdateStatusText.Text = $"发现新版本 {UpdateService.LatestVersionText}（当前 {_currentVersionText}）。点击 [立即更新] 下载并安装。";
            UpdateActionButton.IsEnabled = true;
        }
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateActionButton.IsEnabled = false;
        UpdateStatusText.Text = "正在从 GitHub Release 检查更新...";

        var result = await UpdateService.CheckAsync();
        UpdateStatusText.Text = result.Message;
        UpdateActionButton.IsEnabled = result.IsNewer && result.HasInstaller;
    }

    private async void UpdateActionButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateActionButton.IsEnabled = false;
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在下载更新...";
        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdateProgressBar.Value = 0;

        _downloadCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            var progress = new Progress<double>(percent =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateProgressBar.Value = percent;
                    UpdateStatusText.Text = $"正在下载更新... {percent:F0}%";
                });
            });

            var installerPath = await UpdateService.DownloadInstallerAsync(progress);

            UpdateProgressBar.Visibility = Visibility.Collapsed;
            UpdateStatusText.Text = "下载完成，正在启动安装程序...";

            UpdateService.LaunchInstaller(installerPath);
        }
        catch (OperationCanceledException)
        {
            UpdateProgressBar.Visibility = Visibility.Collapsed;
            UpdateStatusText.Text = "下载超时，请重试。";
            UpdateActionButton.IsEnabled = true;
            CheckUpdateButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            UpdateProgressBar.Visibility = Visibility.Collapsed;
            UpdateStatusText.Text = $"下载失败：{ex.Message}";
            UpdateActionButton.IsEnabled = true;
            CheckUpdateButton.IsEnabled = true;
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }
}