using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CoreCage.Core.Detection
{
    /// <summary>
    /// Captures LIVE system signals into a <see cref="SignalSnapshot"/> for the pure
    /// <see cref="ConfidenceClassifier"/> to consume. This is the I/O side of the split:
    /// every system call (Win32 foreground window, GPU/CPU load,
    /// process classification) lives here, so the classifier stays a deterministic, testable
    /// function of its input. Nothing in this type is required for the classifier to compile
    /// or run on synthetic data.
    ///
    /// <para>Construction is cheap and side-effect-free; all work happens in <see cref="Capture"/>.
    /// Threading: <see cref="Capture"/> does blocking I/O (WMI/LHM refresh)
    /// — call it from a background thread, never the UI
    /// dispatcher. It is safe to call repeatedly; it allocates only the returned struct.</para>
    ///
    /// <para>Signals are wired to real sources where one exists today; the few that need a
    /// not-yet-built capture pipeline (raw-input cadence, presented-frame rate) fall back to a
    /// safe default and are marked with <c>// TODO(wire):</c> plus a settable hook so the app
    /// can feed them once that plumbing lands — without ever blocking compilation.</para>
    /// </summary>
    public sealed class SignalCollector
    {
        /// <summary>
        /// Optional live input-cadence provider (raw mouse + keyboard events per second).
        /// There is no global low-level input counter in the codebase yet, so this defaults
        /// to <c>null</c> and the corresponding signal reports 0. Wire a real raw-input hook
        /// (e.g. a <c>WM_INPUT</c> listener on the UI thread feeding a rolling counter) and
        /// assign it here. TODO(wire): replace with a real raw-input rate source.
        /// </summary>
        public Func<double>? InputRateProvider { get; set; }

        /// <summary>
        /// Optional live presented-FPS provider. <see cref="Telemetry.FrametimeStats"/> can turn
        /// PresentMon frametimes into an FPS figure, but nothing streams those frametimes live to
        /// this collector yet, so this defaults to <c>null</c> and the signal reports 0. Assign a
        /// provider backed by the live PresentMon capture once it is running.
        /// TODO(wire): replace with a real live presented-FPS source.
        /// </summary>
        public Func<double>? FramesPerSecProvider { get; set; }

        /// <summary>
        /// Optional clock override (UTC) for deterministic testing of <see cref="SignalSnapshot.FocusChangedMsAgo"/>.
        /// Defaults to <see cref="DateTime.UtcNow"/>.
        /// </summary>
        public Func<DateTime> NowUtc { get; set; } = () => DateTime.UtcNow;

        // Tracks the most recent foreground exe + when it changed, so FocusChangedMsAgo can be
        // derived even without an event hook: Capture() polls the foreground and remembers the
        // last transition. If a ForegroundWatcher event drives NoteForegroundChanged() instead,
        // that timestamp wins.
        private string? _lastForegroundExe;
        private DateTime _lastForegroundChangeUtc;

        public SignalCollector()
        {
            _lastForegroundChangeUtc = DateTime.MinValue;
        }

        /// <summary>
        /// Push a foreground-change notification from an external source (e.g. the
        /// <see cref="Profiles.ForegroundWatcher"/> event), so <see cref="SignalSnapshot.FocusChangedMsAgo"/>
        /// reflects the real transition instant rather than a poll boundary. Optional — if never
        /// called, <see cref="Capture"/> detects transitions itself by polling.
        /// </summary>
        /// <param name="exe">The new foreground exe name (e.g. "cs2.exe").</param>
        public void NoteForegroundChanged(string? exe)
        {
            if (!string.Equals(exe, _lastForegroundExe, StringComparison.OrdinalIgnoreCase))
            {
                _lastForegroundExe = exe;
                _lastForegroundChangeUtc = NowUtc();
            }
        }

        /// <summary>
        /// Captures a single snapshot of the current live signals. Pure-output: returns a fresh
        /// <see cref="SignalSnapshot"/> and mutates only this collector's focus-tracking bookkeeping.
        /// Any individual signal that cannot be read degrades to a safe default rather than throwing.
        /// </summary>
        // Self-ignore + last-real-session hold (fixes the deck "view paradox").
        private readonly string _selfProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        private SignalSnapshot _lastSnapshot;
        private bool _hasLast;

        public SignalSnapshot Capture()
        {
            DateTime now = NowUtc();

            // ── Stage 1: foreground window identity + focus recency ──────────────
            IntPtr hwnd = GetForegroundWindow();
            string exe = ResolveForegroundExe(hwnd, out string processName);

            // SELF-IGNORE (fixes the "view paradox"): tabbing to CoreCage's OWN window to look at the
            // deck is NOT a new activity session — return the last REAL session so the deck holds (e.g. stays
            // Gaming) instead of snapping to Normal just because you glanced at it.
            if (_hasLast && string.Equals(processName, _selfProcessName, StringComparison.OrdinalIgnoreCase))
                return _lastSnapshot;

            // Self-detect focus transitions when no external notifier drives them.
            if (!string.Equals(exe, _lastForegroundExe, StringComparison.OrdinalIgnoreCase))
            {
                _lastForegroundExe = exe;
                _lastForegroundChangeUtc = now;
            }

            int focusChangedMsAgo = _lastForegroundChangeUtc == DateTime.MinValue
                ? int.MaxValue
                : (int)Math.Clamp((now - _lastForegroundChangeUtc).TotalMilliseconds, 0.0, (double)int.MaxValue);

            bool isFullscreen = IsForegroundFullscreen(hwnd);

            // ── Stage 2: workload (GPU/CPU load, input cadence, frame pacing) ────
            double gpuLoadPct = SafeDouble(() => SystemMonitor.GetGpuUsage());
            double cpuLoadPct = SafeDouble(() => SystemMonitor.GetCpuUsage());
            double inputRatePerSec = SafeDouble(() => InputRateProvider?.Invoke() ?? 0.0); // TODO(wire): raw-input cadence
            double framesPerSec = SafeDouble(() => FramesPerSecProvider?.Invoke() ?? 0.0); // TODO(wire): live presented FPS

            // ── Stage 3: process identity / launcher context ──────────────────────
            LauncherContext launcher = DetectLauncherContext(processName);
            bool compilerOrTerminalActive =
                launcher is LauncherContext.VsCode or LauncherContext.Ide or LauncherContext.Terminal;

            int focusedMonitorCount = SafeInt(GetMonitorCount, fallback: 1);

            var snapshot = new SignalSnapshot
            {
                ForegroundExe = exe,
                ForegroundProcessName = processName,
                IsFullscreen = isFullscreen,
                FocusChangedMsAgo = focusChangedMsAgo,

                GpuLoadPct = gpuLoadPct,
                InputRatePerSec = inputRatePerSec,
                FramesPerSec = framesPerSec,
                CpuLoadPct = cpuLoadPct,

                LauncherContext = launcher,
                CompilerOrTerminalActive = compilerOrTerminalActive,
                FocusedMonitorCount = focusedMonitorCount,
            };
            _lastSnapshot = snapshot;   // remember the last REAL session for self-ignore
            _hasLast = true;
            return snapshot;
        }

        // ── Foreground exe / process name ────────────────────────────────────────
        private static string ResolveForegroundExe(IntPtr hwnd, out string processName)
        {
            processName = string.Empty;
            try
            {
                if (hwnd == IntPtr.Zero) return string.Empty;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return string.Empty;
                using Process p = Process.GetProcessById((int)pid);
                processName = p.ProcessName;          // base name, e.g. "Code", "NinjaTrader"
                return p.ProcessName + ".exe";        // exe form the classifier expects, e.g. "cs2.exe"
            }
            catch
            {
                return string.Empty;
            }
        }

        // ── Fullscreen / borderless-fullscreen detection ─────────────────────────
        // True when the foreground window's rect covers (or exceeds) the bounds of the monitor it
        // sits on. Catches both exclusive and borderless-fullscreen because both fill the monitor.
        private static bool IsForegroundFullscreen(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return false;

                // Ignore the desktop / shell window — a maximized Explorer desktop is not "fullscreen app".
                if (hwnd == GetDesktopWindow() || hwnd == GetShellWindow()) return false;

                if (!GetWindowRect(hwnd, out RECT win)) return false;

                IntPtr mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (mon == IntPtr.Zero) return false;

                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(mon, ref mi)) return false;

                RECT screen = mi.rcMonitor;

                // Window covers the whole monitor (allow a tiny tolerance for off-by-one borders).
                const int tol = 2;
                return win.Left   <= screen.Left   + tol &&
                       win.Top    <= screen.Top    + tol &&
                       win.Right  >= screen.Right  - tol &&
                       win.Bottom >= screen.Bottom - tol;
            }
            catch
            {
                return false;
            }
        }

        // ── Launcher / IDE context ───────────────────────────────────────────────
        // Reuses ProcessWatcher's game classification, then refines the
        // game side (Steam vs Epic vs other store) and the dev side (VS Code / IDE / Terminal) from
        // the process base name. Pure switch on a known set of names — anything unknown -> None.
        private static LauncherContext DetectLauncherContext(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return LauncherContext.None;
            string lower = processName.Trim().ToLowerInvariant();

            // Dev tooling — match before the game classifier (these are on its protected list).
            if (lower is "code" or "code - insiders") return LauncherContext.VsCode;
            if (lower is "devenv" or "rider64" or "idea64" or "clion64" or "webstorm64"
                or "pycharm64" or "phpstorm64" or "goland64")
                return LauncherContext.Ide;
            if (lower is "windowsterminal" or "wt" or "cmd" or "powershell" or "pwsh"
                or "conhost" or "bash" or "wsl" or "alacritty" or "wezterm")
                return LauncherContext.Terminal;

            // Game stores — split the broad "game" classification into store-specific context.
            if (CoreCage.Core.ProcessWatcher.IsGameProcess(processName))
            {
                if (lower.Contains("steam")) return LauncherContext.Steam;
                if (lower.Contains("epic") || lower.Contains("fortnite")) return LauncherContext.Epic;
                // TODO(wire): per-store detection currently leans on the foreground exe name; a
                // parent-process/install-path walk (ProcessWatcher already knows the store paths)
                // would attribute store more precisely. Safe default below: generic game store.
                return LauncherContext.OtherGameStore;
            }

            return LauncherContext.None;
        }

        // ── Monitor count (active displays in the session) ───────────────────────
        private static int GetMonitorCount()
        {
            int count = 0;
            // Lambda has no captured state issues; increments the closed-over local via ref-like counter.
            bool ok = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr _, IntPtr _, ref RECT _, IntPtr _) => { count++; return true; },
                IntPtr.Zero);
            return ok && count > 0 ? count : 1;
        }

        // ── Safe wrappers — never let one bad signal abort the whole snapshot ────
        private static double SafeDouble(Func<double> read)
        {
            try { double v = read(); return double.IsNaN(v) || double.IsInfinity(v) ? 0.0 : v; }
            catch { return 0.0; }
        }

        private static int SafeInt(Func<int> read, int fallback)
        {
            try { return read(); }
            catch { return fallback; }
        }

        // ── Win32 interop ────────────────────────────────────────────────────────
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
            MonitorEnumProc lpfnEnum, IntPtr dwData);
    }
}
