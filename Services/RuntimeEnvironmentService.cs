using System.Diagnostics;
using System.Text.RegularExpressions;
using FControl.Models;

namespace FControl.Services;

public sealed class RuntimeEnvironmentService
{
    private readonly AppConfigurationService _configurationService;

    public RuntimeEnvironmentService(AppConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public async Task<RuntimeDetectionResult> DetectPythonAsync()
    {
        var customPath = _configurationService.Current.RuntimePaths.PythonPath;
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            return await TestExecutableAsync("Python", customPath, "--version");
        }

        return await DetectPythonAutoAsync();
    }

    public async Task<RuntimeDetectionResult> DetectPythonAutoAsync()
    {
        var launcher = await TryRunAsync("py", "-0p");
        if (launcher.Succeeded)
        {
            foreach (var line in launcher.Output.Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var executable = ExtractWindowsExecutablePath(line, "python");
                if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
                {
                    var tested = await TestExecutableAsync("Python", executable, "--version");
                    if (tested.IsAvailable)
                    {
                        return tested;
                    }
                }
            }
        }

        foreach (var candidate in new[] { ("python", "--version"), ("python3", "--version"), ("py", "-3 --version") })
        {
            var result = await TestExecutableAsync("Python", candidate.Item1, candidate.Item2);
            if (result.IsAvailable)
            {
                return result;
            }
        }

        return RuntimeDetectionResult.Unavailable("Python", "未检测到 Python。请安装 Python，或在右侧填写 python.exe 路径。");
    }

    public async Task<RuntimeDetectionResult> DetectNodeAsync()
    {
        var customPath = _configurationService.Current.RuntimePaths.NodePath;
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            return await TestExecutableAsync("Node.js", customPath, "--version");
        }

        return await DetectNodeAutoAsync();
    }

    public async Task<RuntimeDetectionResult> DetectNodeAutoAsync()
    {
        var result = await TestExecutableAsync("Node.js", "node", "--version");
        if (result.IsAvailable)
        {
            return result;
        }

        return RuntimeDetectionResult.Unavailable("Node.js", "未检测到 Node.js。请安装 Node.js，或在右侧填写 node.exe 路径。");
    }

    public async Task<RuntimeDetectionResult> TestExecutableAsync(string name, string executablePath, string versionArguments)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return RuntimeDetectionResult.Unavailable(name, "路径为空。");
        }

        var normalizedPath = Environment.ExpandEnvironmentVariables(executablePath.Trim('"'));
        if (LooksLikePath(normalizedPath) && !File.Exists(normalizedPath))
        {
            return RuntimeDetectionResult.Unavailable(name, $"路径不存在：{executablePath}");
        }

        var run = await TryRunAsync(normalizedPath, versionArguments);
        if (!run.Succeeded)
        {
            return RuntimeDetectionResult.Unavailable(name, run.Error);
        }

        var version = (run.Output + Environment.NewLine + run.Error)
            .Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim() ?? "可用";
        var fullPath = await ResolveExecutablePathAsync(normalizedPath);
        return RuntimeDetectionResult.Available(name, fullPath, version);
    }

    private static async Task<string> ResolveExecutablePathAsync(string executablePath)
    {
        if (LooksLikePath(executablePath))
        {
            return Path.GetFullPath(executablePath);
        }

        var where = await TryRunAsync("where", executablePath);
        if (where.Succeeded)
        {
            var resolved = where.Output
                .Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.Trim())
                .FirstOrDefault(File.Exists);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return executablePath;
    }

    private static string ExtractWindowsExecutablePath(string line, string executableNamePart)
    {
        var match = Regex.Match(
            line,
            $@"[A-Za-z]:\\[^\r\n]*?{Regex.Escape(executableNamePart)}[^\\\s]*\.exe",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Value.Trim() : string.Empty;
    }

    private static async Task<RuntimeProbeRun> TryRunAsync(string fileName, string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start())
            {
                return RuntimeProbeRun.Failure("进程启动失败。");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exited = await Task.Run(() => process.WaitForExit(5000));
            if (!exited)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // ignore
                }

                return RuntimeProbeRun.Failure("检测命令超时。");
            }

            var output = await outputTask;
            var error = await errorTask;
            return process.ExitCode == 0
                ? RuntimeProbeRun.Success(output, error)
                : RuntimeProbeRun.Failure(string.IsNullOrWhiteSpace(error) ? output : error);
        }
        catch (Exception ex)
        {
            return RuntimeProbeRun.Failure(ex.Message);
        }
    }

    private static bool LooksLikePath(string value)
    {
        return value.Contains(':') || value.Contains('\\') || value.Contains('/');
    }
}

public sealed record RuntimeDetectionResult(
    string Name,
    bool IsAvailable,
    string Path,
    string Version,
    string Message,
    DateTimeOffset DetectedAt)
{
    public static RuntimeDetectionResult Available(string name, string path, string version)
    {
        return new RuntimeDetectionResult(name, true, path, version, "可用", DateTimeOffset.Now);
    }

    public static RuntimeDetectionResult Unavailable(string name, string message)
    {
        return new RuntimeDetectionResult(name, false, string.Empty, string.Empty, message, DateTimeOffset.Now);
    }
}

internal sealed record RuntimeProbeRun(bool Succeeded, string Output, string Error)
{
    public static RuntimeProbeRun Success(string output, string error)
    {
        return new RuntimeProbeRun(true, output, error);
    }

    public static RuntimeProbeRun Failure(string error)
    {
        return new RuntimeProbeRun(false, string.Empty, error);
    }
}
