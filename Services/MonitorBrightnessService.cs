using System.Runtime.InteropServices;

namespace FControl.Services;

public sealed class MonitorBrightnessService
{
    private const int McCapabilityStringLength = 512;

    public BrightnessControlResult ChangeBrightnessByPercent(int deltaPercent)
    {
        var monitors = new List<PhysicalMonitorHandle>();

        try
        {
            monitors = EnumeratePhysicalMonitors();
            if (monitors.Count == 0)
            {
                return BrightnessControlResult.Failure("未发现可控制的物理显示器。请确认当前显示器通过 HDMI/DP 连接且启用了 DDC/CI。");
            }

            var controlled = 0;
            var unsupported = 0;
            var lastError = string.Empty;
            int? firstBrightness = null;

            foreach (var monitor in monitors)
            {
                if (!TryGetBrightness(monitor.Handle, out var minimum, out var current, out var maximum))
                {
                    unsupported++;
                    lastError = $"{monitor.Description} 不支持读取 DDC/CI 亮度（Win32 错误 {Marshal.GetLastWin32Error()}）。";
                    continue;
                }

                if (maximum <= minimum)
                {
                    unsupported++;
                    lastError = $"{monitor.Description} 返回了无效的亮度范围。";
                    continue;
                }

                var range = maximum - minimum;
                var delta = Math.Max(1u, (uint)Math.Round(range * Math.Abs(deltaPercent) / 100.0));
                var next = deltaPercent >= 0
                    ? Math.Min(maximum, current + delta)
                    : current > delta ? current - delta : minimum;

                if (next < minimum)
                {
                    next = minimum;
                }

                if (!SetMonitorBrightness(monitor.Handle, next))
                {
                    unsupported++;
                    lastError = $"{monitor.Description} 不支持写入 DDC/CI 亮度（Win32 错误 {Marshal.GetLastWin32Error()}）。";
                    continue;
                }

                controlled++;
                firstBrightness ??= (int)Math.Round((next - minimum) * 100.0 / range);
            }

            if (controlled > 0)
            {
                return BrightnessControlResult.Success(firstBrightness ?? 0, controlled, unsupported);
            }

            return BrightnessControlResult.Failure(string.IsNullOrWhiteSpace(lastError)
                ? "当前显示器不支持 DDC/CI 亮度控制。请检查显示器 OSD、线材、扩展坞、KVM 或显卡驱动。"
                : lastError);
        }
        catch (Exception ex)
        {
            return BrightnessControlResult.Failure(ex.Message);
        }
        finally
        {
            foreach (var monitor in monitors)
            {
                monitor.Dispose();
            }
        }
    }

    public IReadOnlyList<MonitorBrightnessInfo> EnumerateMonitors()
    {
        var monitors = new List<PhysicalMonitorHandle>();

        try
        {
            monitors = EnumeratePhysicalMonitors();
            return monitors.Select(static monitor =>
            {
                var isBrightnessSupported = TryGetBrightness(monitor.Handle, out var minimum, out var current, out var maximum);
                var brightness = isBrightnessSupported && maximum > minimum
                    ? (int?)Math.Round((current - minimum) * 100.0 / (maximum - minimum))
                    : null;

                return new MonitorBrightnessInfo(monitor.Description, isBrightnessSupported, brightness);
            }).ToArray();
        }
        finally
        {
            foreach (var monitor in monitors)
            {
                monitor.Dispose();
            }
        }
    }

    public int? GetPrimaryBrightnessPercent()
    {
        return EnumerateMonitors()
            .FirstOrDefault(static monitor => monitor.IsBrightnessSupported && monitor.BrightnessPercent is not null)
            ?.BrightnessPercent;
    }

    private static List<PhysicalMonitorHandle> EnumeratePhysicalMonitors()
    {
        var monitors = new List<PhysicalMonitorHandle>();
        var callback = new MonitorEnumProc((hMonitor, _, _, _) =>
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count) || count == 0)
            {
                return true;
            }

            var physicalMonitors = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physicalMonitors))
            {
                return true;
            }

            monitors.AddRange(physicalMonitors.Select(static monitor =>
                new PhysicalMonitorHandle(monitor.hPhysicalMonitor, monitor.szPhysicalMonitorDescription)));
            return true;
        });

        if (!EnumDisplayMonitors(0, 0, callback, 0))
        {
            throw new InvalidOperationException($"枚举显示器失败（Win32 错误 {Marshal.GetLastWin32Error()}）。");
        }

        return monitors;
    }

    private static bool TryGetBrightness(nint monitor, out uint minimum, out uint current, out uint maximum)
    {
        minimum = 0;
        current = 0;
        maximum = 0;

        return GetMonitorBrightness(monitor, out minimum, out current, out maximum);

    }

    private sealed class PhysicalMonitorHandle(nint handle, string description) : IDisposable
    {
        public nint Handle { get; private set; } = handle;
        public string Description { get; } = string.IsNullOrWhiteSpace(description) ? "未知显示器" : description;

        public void Dispose()
        {
            if (Handle == 0)
            {
                return;
            }

            _ = DestroyPhysicalMonitor(Handle);
            Handle = 0;
        }
    }

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, nint lprcMonitor, nint dwData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public nint hPhysicalMonitor;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint hMonitor, out uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(
        nint hMonitor,
        uint dwPhysicalMonitorArraySize,
        [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitor(nint hMonitor);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(nint hMonitor, out uint pdwMinimumBrightness, out uint pdwCurrentBrightness, out uint pdwMaximumBrightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(nint hMonitor, uint dwNewBrightness);
}

public sealed record BrightnessControlResult(
    bool Succeeded,
    int BrightnessPercent,
    int ControlledMonitorCount,
    int UnsupportedMonitorCount,
    string? Message)
{
    public static BrightnessControlResult Success(int brightnessPercent, int controlledMonitorCount, int unsupportedMonitorCount)
    {
        return new BrightnessControlResult(true, Math.Clamp(brightnessPercent, 0, 100), controlledMonitorCount, unsupportedMonitorCount, null);
    }

    public static BrightnessControlResult Failure(string message)
    {
        return new BrightnessControlResult(false, 0, 0, 0, message);
    }
}

public sealed record MonitorBrightnessInfo(string Description, bool IsBrightnessSupported, int? BrightnessPercent);
