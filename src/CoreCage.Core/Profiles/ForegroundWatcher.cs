using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CoreCage.Core.Profiles
{
    /// <summary>
    /// Raises <see cref="ForegroundExeChanged"/> with the foreground process's exe name whenever the
    /// active window changes, via a system-wide <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c> — the
    /// zero-poll, read-only, anti-cheat-safe way to drive per-game auto-profiles (no injection, never
    /// touches the game process). Must be created/started on a thread with a message pump (the WPF UI
    /// thread). READY but not started by the app yet — wiring it (and acting on the match) is the
    /// integration step.
    /// </summary>
    public sealed class ForegroundWatcher : IDisposable
    {
        public event Action<string>? ForegroundExeChanged;

        private IntPtr _hook;
        private WinEventDelegate? _proc; // field keeps the delegate alive for the hook's lifetime

        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _proc = OnWinEvent;
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
            Logger.Log(_hook != IntPtr.Zero ? "ForegroundWatcher started" : "ForegroundWatcher failed to hook");
        }

        public void Stop()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
            _proc = null;
        }

        public void Dispose() => Stop();

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            string? exe = ResolveExe(hwnd);
            if (!string.IsNullOrEmpty(exe)) ForegroundExeChanged?.Invoke(exe!);
        }

        private static string? ResolveExe(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return null;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return null;
                using Process p = Process.GetProcessById((int)pid);
                return p.ProcessName + ".exe";
            }
            catch { return null; }
        }

        // ── Win32 ──────────────────────────────────────────────────────────────
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
