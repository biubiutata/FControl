using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FControl.Services;

public static class UpdateService
{
    private const string RepositoryUrl = "https://github.com/biubiutata/FControl";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/biubiutata/FControl/releases/latest";
    private const string ReleasesUrl = RepositoryUrl + "/releases";
    private static readonly HttpClient HttpClient = new();
    private static readonly Regex VersionRegex = new(@"\d+(?:\.\d+){0,3}", RegexOptions.Compiled);

    public static bool IsAvailable { get; private set; }
    public static string? LatestVersionText { get; private set; }
    public static string? LatestReleaseUrl { get; private set; }
    public static string? InstallerDownloadUrl { get; private set; }
    public static event Action? UpdateAvailableChanged;

    public static async Task<CheckResult> CheckAsync()
    {
        try
        {
            var currentVersionText = GetCurrentVersionText();
            var latestRelease = await FetchLatestReleaseAsync();
            var currentVersion = TryParseVersion(currentVersionText);
            var latestVersion = TryParseVersion(latestRelease.TagName);

            if (latestVersion is null)
            {
                return new CheckResult(false, $"已获取最新 Release：{latestRelease.DisplayName}，但无法解析版本号。", latestRelease, false);
            }

            if (currentVersion is null)
            {
                return new CheckResult(false, $"最新版本：{latestRelease.DisplayName}。当前版本 [{currentVersionText}] 无法用于自动比较。", latestRelease, false);
            }

            var isNewer = latestVersion.CompareTo(currentVersion) > 0;
            var hasInstaller = !string.IsNullOrWhiteSpace(latestRelease.InstallerAssetUrl);
            var message = isNewer
                ? $"发现新版本 {latestRelease.DisplayName}（当前 {currentVersionText}）。点击 [立即更新] 下载并安装。"
                : $"已是最新版本（当前 {currentVersionText}，GitHub 最新 {latestRelease.TagName}）。";

            SetAvailable(isNewer, latestRelease.DisplayName);
            LatestReleaseUrl = latestRelease.HtmlUrl;
            InstallerDownloadUrl = latestRelease.InstallerAssetUrl;

            return new CheckResult(isNewer, message, latestRelease, hasInstaller);
        }
        catch (TaskCanceledException)
        {
            return new CheckResult(false, "检查更新失败：请求超时。", null, false);
        }
        catch (Exception ex)
        {
            return new CheckResult(false, $"检查更新失败：{ex.Message}", null, false);
        }
    }

    public static string ReleasesPageUrl => ReleasesUrl;
    public static string RepositoryPageUrl => RepositoryUrl;

    public static void ClearAvailable()
    {
        IsAvailable = false;
        LatestVersionText = null;
        InstallerDownloadUrl = null;
    }

    public static async Task<string> DownloadInstallerAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var url = InstallerDownloadUrl ?? throw new InvalidOperationException("没有可用的安装器下载地址。");
        var tempDir = Path.GetTempPath();
        var fileName = $"FControl-Update-{Guid.NewGuid():N}.exe";
        var filePath = Path.Combine(tempDir, fileName);

        try
        {
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous);

            var buffer = new byte[8192];
            var downloadedBytes = 0L;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    progress?.Report((double)downloadedBytes / totalBytes * 100);
                }
            }

            return filePath;
        }
        catch
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
            }

            throw;
        }
    }

    public static void LaunchInstaller(string filePath)
    {
        Process.Start(new ProcessStartInfo(filePath)
        {
            UseShellExecute = true
        });
    }

    public static void LaunchInstallerAfterCurrentProcessExits(string filePath)
    {
        var currentProcessId = Environment.ProcessId;
        var escapedFilePath = EscapePowerShellSingleQuotedString(filePath);
        var command = $"Wait-Process -Id {currentProcessId}; Start-Process -FilePath '{escapedFilePath}'";

        Process.Start(new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"{command}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static void SetAvailable(bool available, string? versionText)
    {
        IsAvailable = available;
        LatestVersionText = versionText;
        if (available)
        {
            UpdateAvailableChanged?.Invoke();
        }
    }

    public static string GetCurrentVersionText()
    {
        var informationalVersion = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+')[0];
        }

        return typeof(App).Assembly.GetName().Version?.ToString(3) ?? "dev";
    }

    public static string GetInstallerArchName()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => "win-x64"
        };
    }

    private static async Task<ReleaseInfo> FetchLatestReleaseAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("User-Agent", "FControl-Updater");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        using var response = await HttpClient.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        var root = document.RootElement;
        var tagName = GetJsonProperty(root, "tag_name");
        var htmlUrl = GetJsonProperty(root, "html_url");
        var name = GetJsonProperty(root, "name");

        if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(htmlUrl))
        {
            throw new InvalidOperationException("GitHub Release response missing version or URL.");
        }

        var archName = GetInstallerArchName();
        var installerUrl = string.Empty;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var assetName = GetJsonProperty(asset, "name");
                if (!string.IsNullOrWhiteSpace(assetName) && assetName.Contains(archName, StringComparison.OrdinalIgnoreCase))
                {
                    installerUrl = GetJsonProperty(asset, "browser_download_url");
                    break;
                }
            }
        }

        return new ReleaseInfo(tagName, name, htmlUrl, installerUrl);
    }

    private static string GetJsonProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string EscapePowerShellSingleQuotedString(string value)
    {
        return value.Replace("'", "''");
    }

    internal static Version? TryParseVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = VersionRegex.Match(value);
        if (!match.Success)
        {
            return null;
        }

        var parts = match.Value.Split('.').Select(static part => int.TryParse(part, out var number) ? number : -1).ToList();
        if (parts.Any(static part => part < 0))
        {
            return null;
        }

        while (parts.Count < 4)
        {
            parts.Add(0);
        }

        return new Version(parts[0], parts[1], parts[2], parts[3]);
    }

    public sealed record ReleaseInfo(string TagName, string Name, string HtmlUrl, string InstallerAssetUrl)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? TagName : $"{Name}（{TagName}）";
    }

    public sealed record CheckResult(bool IsNewer, string Message, ReleaseInfo? Release, bool HasInstaller);
}
