using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace CoreCage.Core
{
    /// <summary>
    /// Monitors for process launches and automatically applies the gaming profile.
    /// Detection uses four signals in priority order:
    ///   1. Process name pattern (fast, catches well-known games)
    ///   2. Install path (Steam/Epic/GOG/Xbox/EA/Ubisoft — catches any store game by location)
    ///   3. Graphics API modules (d3d11/d3d12/vulkan — catches any renderer regardless of name)
    ///   4. FileVersionInfo company/description (catches publisher-signed executables)
    /// </summary>
    public static class ProcessWatcher
    {
        private static ManagementEventWatcher? _watcher;
        private static ManagementEventWatcher? _exitWatcher;
        private static bool _isWatching;
        private static string? _activeGameProcessName;

        // ── Exit polling ─────────────────────────────────────────────────────
        // Auto-restore when the active game closes, WITHOUT enabling the risky
        // auto-START detection (AutoApplyGameProfiles, default OFF) and WITHOUT
        // relying on Win32_ProcessStopTrace (NT-Kernel ETW), which contends with
        // CapFrameX/PresentMon that Gaming Mode launches. A simple poll of the
        // registered game's liveness is reliable on every rig. Started by
        // SetActiveGame (manual Gaming Mode), stopped once the game is gone.
        private static System.Timers.Timer? _exitPollTimer;
        private const int ExitPollIntervalMs = 3000;

        /// <summary>Normalizes a process name for comparison: strips a trailing ".exe" and trims. Pure/testable.</summary>
        public static string StripExe(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var n = name.Trim();
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            return n.Trim();
        }

        // ── Debounce — prevent re-apply storms during game startup ───────────
        // Only fire the full pipeline once per this interval (seconds).
        private const int PipelineDebounceSeconds = 30;
        private static DateTime _lastPipelineFireTime = DateTime.MinValue;
        private static readonly object _debounceLock = new object();

        public static Action<string>? FullGamingModeAction;
        public static Action? FullRestoreAction;
        public static event Action<string>? OnGameDetected;
        public static event Action<string>? OnGameExited;

        // ── Signal 1: Known name fragments (fast path) ───────────────────────
        private static readonly string[] GameNamePatterns = {
            "cs2", "csgo", "counter-strike",
            "r5apex", "apex",
            "valorant", "riotclient",
            "fortnite",
            "pubg", "tslgame",
            "dota2", "dota",
            "overwatch",
            "modernwarfare", "blackops", "warzone", "cod",
            "battlefield", "bf6", "bf2042", "bfv", "bf1",
            "escapefromtarkov", "tarkov",
            "rainbowsix",
            "destiny2",
            "halo_mcc",
            "rocketleague",
            "deadlock",
            "helldivers",
            "playrustclient",
            "palworld",
            "splitgate",
            "warthunder",
            "leagueclient", "league of legends",
            // Arc Raiders (Embark Studios, UE5 — Steam; internal name PioneerGame)
            "arcraiders", "arc_raiders", "pioneergame",
        };

        // ── Signal 2: Game store install paths ───────────────────────────────
        // Any EXE launched from these folders is almost certainly a game.
        private static readonly string[] GameStorePaths = {
            @"\steam\steamapps\common\",
            @"\epic games\",
            @"\gog galaxy\games\",
            @"\xboxgames\",
            @"\ea games\",
            @"\ea desktop\",
            @"\origin games\",
            @"\ubisoft game launcher\games\",
            @"\ubisoft connect\games\",
            @"\riot games\",
            @"\battle.net\",
            @"\rockstar games\",
            @"\bethesda.net launcher\games\",
            @"\amazon games\",
            @"\itchio\",
            @"\heroic\",
            @"\playnite\",
        };

        // ── Signal 3: Graphics API DLLs (loaded modules) ─────────────────────
        // Any process with one of these loaded is rendering a 3D scene.
        private static readonly HashSet<string> GraphicsApiDlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "d3d11.dll", "d3d12.dll", "vulkan-1.dll", "opengl32.dll", "dxgi.dll"
        };

        // ── Signal 4: Known game publisher names in FileVersionInfo ───────────
        private static readonly string[] GamePublisherKeywords = {
            "valve", "ubisoft", "electronic arts", "activision", "blizzard",
            "riot games", "epic games", "2k games", "take-two", "bethesda",
            "id software", "bungie", "respawn", "dice", "rockstar",
            "cd projekt", "obsidian", "insomniac", "naughty dog",
            "505 games", "devolver", "paradox", "focus entertainment",
        };

        // ── Process names to never classify as games or boost ────────────────
        private static readonly HashSet<string> SystemProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Windows OS
            "svchost", "csrss", "wininit", "winlogon", "lsass", "services",
            "explorer", "taskhost", "dwm", "conhost", "smss", "system",
            "registry", "spoolsv", "searchindexer", "msiexec", "taskmgr",
            // Browsers
            "msedge", "chrome", "firefox", "brave", "opera",
            // IDEs and dev tools — use DirectX/GPU acceleration but are NOT games
            "devenv",                               // Visual Studio
            "code",                                 // VS Code
            "code - insiders",
            "code-tunnel",                          // VS Code Remote Tunnel
            "servicehub.intellicodemodelservice",   // VS Code AI service
            "servicehub.roslyncodeanalysisservice", // VS Code Roslyn
            "microsoft.codeanalysis.languageserver",// Roslyn LSP
            "msbuild", "csc", "vbcscompiler",
            "rider64",                              // JetBrains Rider
            "idea64", "clion64", "webstorm64",
            "gpu_encoder_helper",                   // GPU driver encoder helper
            // Hardware monitoring tools (use D3D for OSD)
            "msiafterburner", "rtss",               // MSI Afterburner + RTSS overlay
            "hwinfo64", "hwmonitor", "cpuid",
            "coretemp",
            // FPS / overlay / capture tools — load D3D/graphics DLLs, and some are LAUNCHED BY Gaming Mode
            // itself (CapFrameX). If classified as games they create a detect→launch→detect loop and fire a
            // spurious Restore on exit, thrashing the mode and undoing the CPU power/CO tuning.
            "capframex", "presentmon", "presentmon64-x64", "presentmonservice",
            "encoderserver",                        // NVIDIA encoder helper
            "nvidia overlay", "nvidia share", "nvcontainer", "nvsphelper64", "nvdisplay.container",
            "gameoverlayui", "gameoverlayui64",     // Steam overlay
            "gamebar", "gamebarpresencewriter", "gamebarftserver",  // Windows Game Bar
            // CoreCage itself — never self-boost
            "CoreCage",
            // Steam / Epic launcher helpers — launchers, not games
            "steam", "steamwebhelper", "steamservice",
            "epicgameslauncher", "epicwebhelper",
            // Peripheral / RGB / audio utilities — load D3D/DXGI for their own UI/OSD
            // but are NOT games. (SteelSeries GG was wrongly triggering Gaming Mode.)
            "steelseriesgg", "steelseriesggclient", "steelseriessonar", "steelseriesengine3",
            "razer synapse", "razersynapse", "rzsynapse", "razercentralservice",
            "lghub", "lghub_agent", "logioptionsplus", "logitech gaming software",
            "icue", "corsair.service", "asusframeworkservice", "armourycrate", "armoury crate",
        };

        public static void SetActiveGame(string processName)
        {
            _activeGameProcessName = StripExe(processName);
            StartExitPolling();
        }

        /// <summary>
        /// Forget the registered game and stop exit polling. Call when the user MANUALLY leaves
        /// Gaming Mode (Restore) so closing the game later can't yank them out of the mode
        /// they deliberately chose.
        /// </summary>
        public static void ClearActiveGame()
        {
            _activeGameProcessName = null;
            StopExitPolling();
        }

        /// <summary>Begin polling the registered game's liveness so Gaming Mode auto-restores on exit.</summary>
        private static void StartExitPolling()
        {
            if (_exitPollTimer != null) return;
            try
            {
                _exitPollTimer = new System.Timers.Timer(ExitPollIntervalMs) { AutoReset = true };
                _exitPollTimer.Elapsed += (_, __) => PollActiveGameAlive();
                _exitPollTimer.Start();
                Logger.Log($"ProcessWatcher: exit polling started for '{_activeGameProcessName}' (auto-restore on game exit)");
            }
            catch (Exception ex) { Logger.LogError("StartExitPolling failed", ex); }
        }

        private static void StopExitPolling()
        {
            try { _exitPollTimer?.Stop(); _exitPollTimer?.Dispose(); } catch { }
            _exitPollTimer = null;
        }

        private static void PollActiveGameAlive()
        {
            try
            {
                var name = _activeGameProcessName;
                if (string.IsNullOrEmpty(name)) { StopExitPolling(); return; }

                var procs = Process.GetProcessesByName(name);
                bool alive = procs.Length > 0;
                foreach (var p in procs) p.Dispose();

                if (!alive) HandleGameExited(name);
            }
            catch (Exception ex) { Logger.LogError("PollActiveGameAlive error", ex); }
        }

        /// <summary>Single fire-once exit path shared by the poll timer and the WMI stop-trace.</summary>
        private static void HandleGameExited(string baseName)
        {
            // Guard: only fire if THIS is still the active game (avoids double-restore when both
            // the poller and the WMI watcher observe the same exit).
            if (_activeGameProcessName == null ||
                !string.Equals(StripExe(baseName), _activeGameProcessName, StringComparison.OrdinalIgnoreCase))
                return;

            Logger.Log($"Game exited: {baseName} — triggering auto-restore");
            _activeGameProcessName = null;
            StopExitPolling();
            OnGameExited?.Invoke(baseName);
            FullRestoreAction?.Invoke();
        }

        /// <summary>
        /// Returns the process name of the currently tracked foreground game, or null if none.
        /// Used by MemoryCleaner.Purge to skip the active game's working set.
        /// </summary>
        public static string? GetActiveGameProcessName() => _activeGameProcessName;

        public static void StartWatching()
        {
            if (_isWatching) return;
            try
            {
                var startQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
                _watcher = new ManagementEventWatcher(startQuery);
                _watcher.EventArrived += ProcessStarted;
                _watcher.Start();

                var stopQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace");
                _exitWatcher = new ManagementEventWatcher(stopQuery);
                _exitWatcher.EventArrived += ProcessStopped;
                _exitWatcher.Start();

                _isWatching = true;
                Logger.Log("ProcessWatcher started — path + graphics API + name detection active");
            }
            catch (Exception ex)
            {
                Logger.LogError("ProcessWatcher failed to start (requires elevation)", ex);
            }
        }

        public static void StopWatching()
        {
            // Always stop the exit poller — it runs independently of the gated WMI watchers.
            StopExitPolling();
            if (!_isWatching) return;
            try
            {
                _watcher?.Stop(); _watcher?.Dispose(); _watcher = null;
                _exitWatcher?.Stop(); _exitWatcher?.Dispose(); _exitWatcher = null;
                _isWatching = false;
                Logger.Log("ProcessWatcher stopped");
            }
            catch (Exception ex)
            {
                Logger.LogError("ProcessWatcher stop failed", ex);
            }
        }

        private static void ProcessStarted(object sender, EventArrivedEventArgs e)
        {
            try
            {
                string? rawName = e.NewEvent.Properties["ProcessName"]?.Value?.ToString();
                if (string.IsNullOrEmpty(rawName)) return;

                string processBaseName = rawName.Replace(".exe", "").Trim();
                if (SystemProcesses.Contains(processBaseName)) return;

                ProcessCategory cat = ClassifyProcess(processBaseName);
                if (cat == ProcessCategory.Unknown) return;

                // ── Debounce guard ──────────────────────────────────────────────
                // Games spawn many child processes at startup; only fire the full
                // pipeline once per PipelineDebounceSeconds to avoid re-apply storms.
                lock (_debounceLock)
                {
                    if ((DateTime.UtcNow - _lastPipelineFireTime).TotalSeconds < PipelineDebounceSeconds)
                    {
                        Logger.Log($"ProcessWatcher: debounce — skipping pipeline re-fire for {rawName} " +
                                   $"(last fired {(DateTime.UtcNow - _lastPipelineFireTime).TotalSeconds:F1}s ago)");
                        return;
                    }
                    _lastPipelineFireTime = DateTime.UtcNow;
                }
                // ── End debounce guard ──────────────────────────────────────────

                Logger.Log($"Auto-detected [Gaming]: {rawName}");

                SystemTweaks.ApplyHighPerformancePowerPlan();
                SystemTweaks.SetTimerResolution(true);

                _activeGameProcessName = processBaseName;
                (FullGamingModeAction ?? (n => BoostProcessPriority(n, ProcessPriorityClass.High))).Invoke(processBaseName);

                OnGameDetected?.Invoke(rawName);
            }
            catch (Exception ex)
            {
                Logger.LogError("ProcessStarted handler error", ex);
            }
        }

        private static void ProcessStopped(object sender, EventArrivedEventArgs e)
        {
            try
            {
                string? rawName = e.NewEvent.Properties["ProcessName"]?.Value?.ToString();
                if (string.IsNullOrEmpty(rawName)) return;
                HandleGameExited(StripExe(rawName));
            }
            catch (Exception ex)
            {
                Logger.LogError("ProcessStopped handler error", ex);
            }
        }

        /// <summary>
        /// Classifies a process using four signals. Returns Game or Unknown.
        /// </summary>
        /// <param name="strict">
        /// When true, only strong signals count (name pattern, store path, publisher).
        /// The weak signals — any process loading a DirectX DLL, or "game"/"play" appearing
        /// in metadata — are skipped. Used for bulk priority-boosting so a single peripheral
        /// app can't drag dozens of unrelated processes into the boost.
        /// </param>
        public static ProcessCategory ClassifyProcess(string processName, bool strict = false)
        {
            if (SystemProcesses.Contains(processName)) return ProcessCategory.Unknown;

            string lower = processName.ToLower();

            // Signal 1 — name patterns (no disk I/O)
            if (GameNamePatterns.Any(p => lower.Contains(p)))
                return ProcessCategory.Game;

            // Signals 2, 3, 4 — need the running process
            try
            {
                var procs = Process.GetProcessesByName(processName);
                foreach (var proc in procs)
                {
                    try
                    {
                        string? exePath = proc.MainModule?.FileName;

                        if (!string.IsNullOrEmpty(exePath))
                        {
                            var fvi = FileVersionInfo.GetVersionInfo(exePath);
                            string combined = $"{fvi.FileDescription} {fvi.CompanyName} {fvi.ProductName}".ToLower();

                            // Signal 4 — publisher
                            if (GamePublisherKeywords.Any(k => combined.Contains(k)))
                                return ProcessCategory.Game;

                            // Signal 2 — install path
                            if (IsInGameStorePath(exePath))
                                return ProcessCategory.Game;

                            // Word-bounded so "display" no longer matches the "play" substring.
                            if (!strict && (ContainsWord(combined, "game") || ContainsWord(combined, "play")))
                                return ProcessCategory.Game;
                        }

                        // Signal 3 — graphics API modules (catches any renderer). Skipped in
                        // strict mode: nearly every modern GUI app loads d3d11/dxgi.
                        if (!strict && UsesGraphicsApi(proc))
                            return ProcessCategory.Game;
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }
            }
            catch { }

            return ProcessCategory.Unknown;
        }

        /// <summary>
        /// Returns all currently running processes that look like games.
        /// Used by Gaming Mode to boost whatever is actually running right now.
        /// </summary>
        public static List<Process> GetRunningGameProcesses(bool strict = false)
        {
            var games = new List<Process>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (SystemProcesses.Contains(p.ProcessName)) continue;
                    if (ClassifyProcess(p.ProcessName, strict) == ProcessCategory.Game)
                        games.Add(p);
                    else
                        p.Dispose();
                }
                catch { }
            }
            return games;
        }

        // Whole-word match so substrings like "display"→"play" don't false-positive.
        private static bool ContainsWord(string haystack, string word) =>
            System.Text.RegularExpressions.Regex.IsMatch(
                haystack, $@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b");

        private static bool IsInGameStorePath(string exePath)
        {
            string lower = exePath.ToLower();
            return GameStorePaths.Any(p => lower.Contains(p));
        }

        private static bool UsesGraphicsApi(Process proc)
        {
            try
            {
                foreach (ProcessModule mod in proc.Modules)
                {
                    if (GraphicsApiDlls.Contains(mod.ModuleName ?? ""))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static void BoostProcessPriority(string processName, ProcessPriorityClass priority)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(processName))
                {
                    try { p.PriorityClass = priority; p.Dispose(); }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// True if the process is on the canonical never-touch list (OS-critical, browsers, IDEs/dev
        /// tools, hardware/overlay utilities, CoreCage itself). Exposed so other tuning paths
        /// (e.g. SystemTweaks.ThrottleForMode) honor the SAME list instead of keeping a divergent copy
        /// — divergence is exactly how dev tools like VS Code got deprioritized by Gaming Mode.
        /// </summary>
        public static bool IsProtectedSystemProcess(string? processName) =>
            !string.IsNullOrEmpty(processName) && SystemProcesses.Contains(StripExe(processName));

        public static bool IsGameProcess(string processName) =>
            ClassifyProcess(processName) == ProcessCategory.Game;
    }

    public enum ProcessCategory { Unknown, Game }
}
