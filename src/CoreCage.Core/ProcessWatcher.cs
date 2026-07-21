using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

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
            "helldivers", "helldivers2",
            "playrustclient",
            "palworld",
            "splitgate",
            "warthunder",
            "leagueclient", "league of legends",
            // Arc Raiders (Embark Studios, UE5 — Steam; internal name PioneerGame)
            "arcraiders", "arc_raiders", "pioneergame",
            // Current-rotation titles whose exe name won't hit any pattern above. The store-path
            // and publisher signals catch the long tail; these are just the fast (no-IO) path for
            // ones people actually run. Fragments are matched via Contains (see ClassifyFromSignals),
            // so "gta5"→"gta5.exe", "b1-win64-shipping"→Black Myth: Wukong, etc.
            "marvelrivals", "thefinals", "deltaforceclient", "grayzone",
            "gta5", "gtav", "rdr2", "cyberpunk2077", "eldenring", "nightreign",
            "bg3", "starfield", "wukong", "b1-win64-shipping",
            "warframe", "pathofexile", "poe2", "lostark", "newworld",
            "seaofthieves", "warhammer", "spacemarine", "darktide", "vermintide",
            "monsterhunter", "mhwilds", "readyornot", "hunt", "dayz",
            "starrail", "genshinimpact", "wutheringwaves", "zenlesszonezero",
            "robloxplayerbeta", "fc25", "nba2k",
        };

        // ── Signal 2: Game store install paths ───────────────────────────────
        // Any EXE launched from these folders is almost certainly a game.
        private static readonly string[] GameStorePaths = {
            // Match Steam's invariant subpath, NOT "\steam\steamapps\" — games on a second drive
            // live under a custom library folder (e.g. "D:\SteamLibrary\steamapps\common\") whose
            // name is arbitrary. "\steamapps\common\" is present for every Steam install location.
            @"\steamapps\common\",
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

                // Community profile auto-apply hook (Task 7): if a community profile (loaded via
                // CommunityProfileLoader.LoadDirectory + ProfileStore) matches rawName, apply it here,
                // gated by CommunityProfileAutoApply.MatchForAutoApply(rawName, communityProfiles,
                // modeCoordinator.Current) so it only fires when the confidence classifier agrees
                // this is a Gaming session. Not wired live yet: ProcessWatcher is static/WMI-driven and
                // doesn't hold a ModeDecision or the loaded community-profile list, so wiring it here
                // for real needs those passed in (e.g. via a static setter, mirroring FullGamingModeAction)
                // rather than a fragile ad-hoc integration. See CommunityProfileAutoApply for the pure,
                // unit-tested matching logic.
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

            // Fast path — name alone, no process access at all. Kept ahead of the IO gather so a
            // well-known game is classified even if every handle to it is blocked.
            string lower = processName.ToLower();
            if (GameNamePatterns.Any(p => lower.Contains(p)))
                return ProcessCategory.Game;

            // Gather the remaining signals from the live process, then hand off to the pure
            // classifier. CRITICAL: the exe path comes from QueryFullProcessImageName (see
            // TryGetExecutablePath) rather than MainModule — MainModule enumerates the target's
            // module list, which EAC/BattlEye/GameGuard block even for an elevated caller, so
            // anti-cheat games used to fall through to the name list ONLY. The path + on-disk
            // FileVersionInfo need no such access, so store-path and publisher detection now work
            // on protected games too.
            try
            {
                var procs = Process.GetProcessesByName(processName);
                foreach (var proc in procs)
                {
                    try
                    {
                        string? exePath = TryGetExecutablePath(proc.Id);
                        if (string.IsNullOrEmpty(exePath))
                        {
                            try { exePath = proc.MainModule?.FileName; } catch { /* blocked — path stays null */ }
                        }

                        string? versionInfoText = null;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            try
                            {
                                var fvi = FileVersionInfo.GetVersionInfo(exePath);
                                versionInfoText = $"{fvi.FileDescription} {fvi.CompanyName} {fvi.ProductName}".ToLower();
                            }
                            catch { /* unreadable version block — leave null */ }
                        }

                        // Signal 3 is the one input that still needs process access; a blocked
                        // enumeration simply reports false and we lean on path/publisher instead.
                        bool usesGraphics = !strict && UsesGraphicsApi(proc);

                        var category = ClassifyFromSignals(processName, exePath, versionInfoText, usesGraphics, strict);
                        if (category == ProcessCategory.Game) return ProcessCategory.Game;
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }
            }
            catch { }

            return ProcessCategory.Unknown;
        }

        /// <summary>
        /// Pure signal classifier — no OS/Process dependency, so it is fully unit-testable and its
        /// rules stay locked by tests. All four detection signals in priority order:
        ///   1. name pattern (fragment match), 2. publisher keyword (on-disk version info),
        ///   3. game-store install path, 4. graphics API in use (non-strict only).
        /// Inputs are pre-gathered by <see cref="ClassifyProcess"/>; any that could not be read
        /// (blocked handle, no version block) arrive as null/false and are simply skipped — never throw.
        /// </summary>
        /// <param name="processName">Bare process name (no ".exe").</param>
        /// <param name="exePath">Full exe path if resolvable, else null.</param>
        /// <param name="versionInfoText">Lower-cased "description company product" from the file, else null.</param>
        /// <param name="usesGraphicsApi">True if the process has a d3d/vulkan/opengl module loaded.</param>
        /// <param name="strict">When true, the weak signals (generic "game"/"play" text, graphics API) are ignored.</param>
        public static ProcessCategory ClassifyFromSignals(
            string processName, string? exePath, string? versionInfoText, bool usesGraphicsApi, bool strict = false)
        {
            if (SystemProcesses.Contains(processName)) return ProcessCategory.Unknown;

            string lower = (processName ?? string.Empty).ToLower();

            // Signal 1 — name patterns (fragment match; "helldivers2" contains "helldivers").
            if (GameNamePatterns.Any(p => lower.Contains(p)))
                return ProcessCategory.Game;

            string combined = versionInfoText ?? string.Empty;

            // Signal 4 — publisher keyword in the on-disk version block.
            if (combined.Length > 0 && GamePublisherKeywords.Any(k => combined.Contains(k)))
                return ProcessCategory.Game;

            // Signal 2 — game-store install path (works on anti-cheat games: path only, no handle).
            if (!string.IsNullOrEmpty(exePath) && IsInGameStorePath(exePath))
                return ProcessCategory.Game;

            // Weak signals — only when not strict.
            if (!strict)
            {
                // Word-bounded so "display" no longer matches the "play" substring.
                if (combined.Length > 0 && (ContainsWord(combined, "game") || ContainsWord(combined, "play")))
                    return ProcessCategory.Game;

                // Signal 3 — a loaded renderer implies a 3D app (browsers/IDEs are on the denylist).
                if (usesGraphicsApi)
                    return ProcessCategory.Game;
            }

            return ProcessCategory.Unknown;
        }

        // ── Reliable exe-path resolution (anti-cheat-safe) ────────────────────
        // QueryFullProcessImageName with PROCESS_QUERY_LIMITED_INFORMATION returns a protected
        // process's image path where Process.MainModule (which needs PROCESS_QUERY_INFORMATION +
        // module enumeration) throws AccessDenied on EAC/BattlEye/GameGuard titles. This is the
        // single change that lets store-path/publisher detection see anti-cheat games.
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder exeName, ref uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        /// <summary>
        /// Best-effort full image path for <paramref name="pid"/> via QueryFullProcessImageName.
        /// Returns null when the process is gone or access is denied even for LIMITED info. Never throws.
        /// </summary>
        public static string? TryGetExecutablePath(int pid)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (handle == IntPtr.Zero) return null;

                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : null;
            }
            catch { return null; }
            finally { if (handle != IntPtr.Zero) CloseHandle(handle); }
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
