using System.Runtime.InteropServices;

namespace CarbonMonitor.Infrastructure.SingleInstance;

/// <summary>
/// Enforces single-instance execution using a named Mutex and signals an existing instance to activate.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string TrayHostWindowTitle = "Carbon Monitor Tray Host";
    private const string MutexName = @"Local\CarbonMonitor_SingleInstance_Mutex_v1";
    private const string ActivateMessageName = "WM_CARBONMONITOR_ACTIVATE_V1";
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    private Mutex? _mutex;
    private bool _acquired;

    public static uint ActivationMessageId { get; } = RegisterWindowMessage(ActivateMessageName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    public bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out _acquired);
        if (!_acquired)
        {
            _mutex.Dispose();
            _mutex = null;
        }
        return _acquired;
    }

    public static void BringExistingInstanceToFront()
    {
        if (ActivationMessageId == 0)
            return;

        var target = FindWindow(null, TrayHostWindowTitle);
        if (target != IntPtr.Zero)
        {
            SendMessageTimeout(target, ActivationMessageId, IntPtr.Zero, IntPtr.Zero, 0x0002, 1500, out _);
            return;
        }

        PostMessage(HwndBroadcast, ActivationMessageId, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_acquired && _mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }
        _mutex?.Dispose();
        _mutex = null;
        _acquired = false;
    }
}
