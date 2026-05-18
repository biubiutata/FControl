using System.Runtime;
using System.Runtime.InteropServices;

namespace FControl.Services;

internal static class BackgroundPerformanceService
{
    private static readonly object Gate = new();
    private static bool _isBackgroundMode;
    private static int _trimInProgress;

    public static void EnterBackgroundMode()
    {
        lock (Gate)
        {
            if (_isBackgroundMode)
            {
                QueueWorkingSetTrim();
                return;
            }

            _isBackgroundMode = true;
        }

        QueueWorkingSetTrim();
    }

    public static void LeaveBackgroundMode()
    {
        lock (Gate)
        {
            if (!_isBackgroundMode)
            {
                return;
            }

            _isBackgroundMode = false;
        }
    }

    private static void QueueWorkingSetTrim()
    {
        if (Interlocked.Exchange(ref _trimInProgress, 1) == 1)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(static _ =>
        {
            try
            {
                Thread.Sleep(250);
                if (!IsBackgroundMode())
                {
                    return;
                }

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (IsBackgroundMode())
                {
                    _ = EmptyWorkingSet(GetCurrentProcess());
                }
            }
            finally
            {
                Interlocked.Exchange(ref _trimInProgress, 0);
            }
        });
    }

    private static bool IsBackgroundMode()
    {
        lock (Gate)
        {
            return _isBackgroundMode;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(nint hProcess);
}
