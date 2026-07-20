using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json;
using CoreCage.Core.Caging;
using CoreCage.Core.Ledger;

namespace CoreCage.Core.Modes
{
    /// <summary>
    /// Built-in "Gaming" IModeModule. Wraps the existing engine pipeline -- Gaming Mode++ (MSI/NIC/
    /// GameDVR/UWP/QoS), EAC-safe IFEO+powercfg+FSO polish for the user's gaming process list,
    /// CoreUnpark (core-unpark + perf floor), and Core Cage (reserve top cores for the game, confine
    /// background processes to the rest -- behind <c>FeatureFlags.CoreCageEnabled</c>) -- behind the
    /// uniform Apply/Revert seam.
    ///
    /// Apply runs the pipeline on a background thread and reports each step via IProgress&lt;string&gt;.
    /// Revert reverses the same layers in the opposite order; if any revert step throws, it falls
    /// back to RestoreEverything.RestoreAll() (the Big Red Button) so a partial revert can never leave
    /// the rig half-tweaked.
    ///
    /// IsActive is persisted to a small JSON flag file (default %LOCALAPPDATA%\CoreCage\mode-state.json)
    /// so a crash mid-mode (process killed before Revert runs) is detectable at the next launch --
    /// the app can offer to finish reverting instead of silently believing nothing was ever applied.
    /// The path is constructor-injectable so tests never touch the real file.
    /// </summary>
    public sealed class GamingMode : IModeModule
    {
        public string Name => "Gaming";

        public string Description =>
            "MSI mode, NIC hardening, GameDVR/background-app/QoS polish, EAC-safe IFEO priority, and core-unpark for the active game.";

        private readonly string _statePath;

        // Pipeline steps as delegates (default to the real engine calls) so the sequencing logic here
        // can be exercised with fakes without touching the OS, without adding a whole strategy/DI
        // framework for a 6-line pipeline.
        private readonly Action _applyGamingModePlusPlus;
        private readonly Action _restoreGamingModePlusPlus;
        private readonly Func<IEnumerable<string>, int> _applyPolish;
        private readonly Func<IEnumerable<string>, int> _restorePolish;
        private readonly Action _applyCoreUnpark;
        private readonly Func<bool> _restoreCoreUnpark;
        private readonly Func<RestoreSummary> _restoreEverything;
        private readonly Func<IEnumerable<string>> _gamingProcessList;
        private readonly Func<int> _applyCoreCage;
        private readonly Func<int> _releaseCoreCage;

        // The plan Core Cage last applied, so Revert releases exactly what Apply caged. In-memory only
        // (mirrors the other steps' "not persisted, Task 11 verifies live" posture) -- a crash between
        // Apply and Revert is the same class of gap RestoreEverything (Big Red Button) exists to catch.
        private CagePlan? _lastCagePlan;

        // Tweak Ledger -- "what's active + what it earned you" (Task 6). Each pipeline layer gets its
        // own stable TweakId so the Optimize page can show/benchmark them independently.
        private TweakLedger _ledger;
        internal const string TweakIdGamingPipeline = "gaming-pipeline";
        internal const string TweakIdEacPolish = "eac-polish";
        internal const string TweakIdCoreUnpark = "core-unpark";
        internal const string TweakIdCoreCage = "core-cage";

        public GamingMode(string? statePath = null)
        {
            _statePath = statePath ?? DefaultStatePath();

            _applyGamingModePlusPlus = GamingModePlusPlus.ApplyAll;
            _restoreGamingModePlusPlus = GamingModePlusPlus.RestoreAll;
            _applyPolish = EacSafePriority.ApplyPolishToGamingList;
            _restorePolish = EacSafePriority.RestorePolishFromGamingList;
            _applyCoreUnpark = CoreUnpark.ApplyAll;
            _restoreCoreUnpark = CoreUnpark.RestoreAll;
            _restoreEverything = RestoreEverything.RestoreAll;
            _gamingProcessList = () => UserProcessLists.GetList("gaming");
            _applyCoreCage = ApplyCoreCageReal;
            _releaseCoreCage = ReleaseCoreCageReal;
            _ledger = TweakLedger.Load(TweakLedger.DefaultPath());
        }

        /// <summary>
        /// Test-only seam (review follow-up, MINOR-1/IMPORTANT-1; extended Task 6 for the ledger): lets
        /// the test project swap every Apply-side and Revert-side delegate for an in-memory fake, plus
        /// inject an in-memory <see cref="TweakLedger"/>, so ApplyAsync/RevertAsync's own
        /// sequencing/recording logic (e.g. "release whenever a cage plan exists, regardless of the
        /// current flag"; "record a ledger row per applied step") can be driven end-to-end without ever
        /// touching the real OS or the real ledger file. The apply-side parameters are optional/null
        /// (defaulting to the real implementations already set by the base constructor) so the original
        /// revert-only fake tests (GamingModeRevertReleaseTests) keep compiling unchanged.
        /// </summary>
        internal GamingMode(
            string? statePath,
            Func<int> releaseCoreCage,
            Func<IEnumerable<string>, int> restorePolish,
            Action restoreGamingModePlusPlus,
            Func<bool> restoreCoreUnpark,
            Func<IEnumerable<string>> gamingProcessList,
            Func<RestoreSummary> restoreEverything,
            Action? applyGamingModePlusPlus = null,
            Func<IEnumerable<string>, int>? applyPolish = null,
            Action? applyCoreUnpark = null,
            Func<int>? applyCoreCage = null,
            TweakLedger? ledger = null)
            : this(statePath)
        {
            _releaseCoreCage = releaseCoreCage;
            _restorePolish = restorePolish;
            _restoreGamingModePlusPlus = restoreGamingModePlusPlus;
            _restoreCoreUnpark = restoreCoreUnpark;
            _gamingProcessList = gamingProcessList;
            _restoreEverything = restoreEverything;
            if (applyGamingModePlusPlus != null) _applyGamingModePlusPlus = applyGamingModePlusPlus;
            if (applyPolish != null) _applyPolish = applyPolish;
            if (applyCoreUnpark != null) _applyCoreUnpark = applyCoreUnpark;
            if (applyCoreCage != null) _applyCoreCage = applyCoreCage;
            if (ledger != null) _ledger = ledger;
        }

        /// <summary>Test-only seam: lets a test simulate "Apply already ran and cached a plan" without
        /// invoking the real, OS-mutating ApplyCoreCageReal.</summary>
        internal CagePlan? LastCagePlanForTests
        {
            get => _lastCagePlan;
            set => _lastCagePlan = value;
        }

        public bool IsActive => LoadState().IsActive;

        public Task<ModeResult> ApplyAsync(IProgress<string>? progress = null)
        {
            return Task.Run(() =>
            {
                var steps = new List<string>();
                try
                {
                    progress?.Report("Gaming Mode++ (MSI/NIC/GameDVR/UWP/QoS) applying...");
                    _applyGamingModePlusPlus();
                    steps.Add("Gaming Mode++ applied");
                    RecordLedgerEntry(TweakIdGamingPipeline);

                    progress?.Report("EAC-safe polish applying to gaming process list...");
                    int polished = _applyPolish(_gamingProcessList());
                    steps.Add($"EAC-safe polish applied ({polished} process(es))");
                    RecordLedgerEntry(TweakIdEacPolish);

                    progress?.Report("Core-unpark + perf-floor applying...");
                    _applyCoreUnpark();
                    steps.Add("Core-unpark applied");
                    RecordLedgerEntry(TweakIdCoreUnpark);

                    int caged = 0;
                    if (FeatureFlags.Current.CoreCageEnabled)
                    {
                        progress?.Report("Core Cage: reserving cores for the game...");
                        caged = _applyCoreCage();
                        steps.Add($"Core Cage applied (caged {caged} process(es))");
                        RecordLedgerEntry(TweakIdCoreCage);
                    }

                    SaveLedger();
                    SaveState(true);
                    progress?.Report("Gaming Mode applied.");
                    string cageNote = FeatureFlags.Current.CoreCageEnabled ? $" -- caged {caged} process(es)" : "";
                    return new ModeResult(true, "Gaming Mode applied" + cageNote, steps);
                }
                catch (Exception ex)
                {
                    // Partial-apply honesty (pre-publish cleanup): a mid-pipeline throw used to stop
                    // here with SOME real tweaks already applied (whichever steps ran before the throw)
                    // while nothing was ever called to persist that -- IsActive kept whatever value it
                    // had before Apply started, so a fresh launch could read "OFF" while the rig was
                    // still partially tweaked. Fall back to RestoreEverything.RestoreAll() (the Big Red
                    // Button), same as RevertAsync's own fallback, so the real system and the persisted
                    // flag both land on a single honest, fully-off state instead of an inconsistent one.
                    Logger.LogError("GamingMode.ApplyAsync failed -- falling back to RestoreEverything", ex);
                    steps.Add("FAILED: " + ex.Message + " -- falling back to full system restore");
                    progress?.Report("An apply step failed; falling back to full system restore (Big Red Button)...");
                    try
                    {
                        RestoreSummary summary = _restoreEverything();
                        steps.Add("RestoreEverything: " + summary);
                        DeactivateAllLedgerEntries();
                        SaveLedger();
                        SaveState(false);
                        progress?.Report("Full system restore complete.");
                        return new ModeResult(false,
                            "Gaming Mode apply failed: " + ex.Message + " -- rolled back via full system restore.", steps);
                    }
                    catch (Exception fallbackEx)
                    {
                        Logger.LogError("GamingMode.ApplyAsync fallback RestoreEverything failed", fallbackEx);
                        steps.Add("FALLBACK FAILED: " + fallbackEx.Message);
                        return new ModeResult(false, "Gaming Mode apply failed: " + ex.Message, steps);
                    }
                }
            });
        }

        public Task<ModeResult> RevertAsync(IProgress<string>? progress = null)
        {
            return Task.Run(() =>
            {
                var steps = new List<string>();
                try
                {
                    int released = 0;
                    // Release is gated on "did Apply actually cage something" (_lastCagePlan != null),
                    // NOT on the CURRENT FeatureFlags.CoreCageEnabled value. Release is idempotent and
                    // always-safe; gating it on the current flag meant flipping Core Cage off after
                    // Apply left everything permanently pinned to the caged mask (review IMPORTANT-1).
                    // Capture the decision before releasing -- ReleaseCoreCageReal nulls _lastCagePlan.
                    bool hadCagePlan = _lastCagePlan != null;
                    if (hadCagePlan)
                    {
                        progress?.Report("Core Cage: releasing cores...");
                        released = _releaseCoreCage();
                        steps.Add($"Core Cage released ({released} process(es))");
                        _ledger.Deactivate(TweakIdCoreCage);
                    }

                    progress?.Report("EAC-safe polish reverting...");
                    int restored = _restorePolish(_gamingProcessList());
                    steps.Add($"EAC-safe polish restored ({restored} process(es))");
                    _ledger.Deactivate(TweakIdEacPolish);

                    progress?.Report("Gaming Mode++ reverting...");
                    _restoreGamingModePlusPlus();
                    steps.Add("Gaming Mode++ reverted");
                    _ledger.Deactivate(TweakIdGamingPipeline);

                    progress?.Report("Core-unpark reverting...");
                    bool coreUnparkRestored = _restoreCoreUnpark();
                    steps.Add(coreUnparkRestored ? "Core-unpark restored" : "Core-unpark: nothing to restore");
                    _ledger.Deactivate(TweakIdCoreUnpark);

                    SaveLedger();
                    SaveState(false);
                    progress?.Report("Gaming Mode reverted.");
                    string cageNote = hadCagePlan ? $" -- released {released} process(es)" : "";
                    return new ModeResult(true, "Gaming Mode reverted" + cageNote, steps);
                }
                catch (Exception ex)
                {
                    Logger.LogError("GamingMode.RevertAsync step failed -- falling back to RestoreEverything", ex);
                    steps.Add("FAILED: " + ex.Message + " -- falling back to full system restore");
                    progress?.Report("A revert step failed; falling back to full system restore (Big Red Button)...");
                    try
                    {
                        RestoreSummary summary = _restoreEverything();
                        steps.Add("RestoreEverything: " + summary);
                        DeactivateAllLedgerEntries();
                        SaveLedger();
                        SaveState(false);
                        progress?.Report("Full system restore complete.");
                        return new ModeResult(true, "Gaming Mode revert fell back to full system restore", steps);
                    }
                    catch (Exception fallbackEx)
                    {
                        Logger.LogError("GamingMode.RevertAsync fallback RestoreEverything failed", fallbackEx);
                        steps.Add("FALLBACK FAILED: " + fallbackEx.Message);
                        return new ModeResult(false, "Gaming Mode revert failed", steps);
                    }
                }
            });
        }

        // ------------------------------------------------------------------
        // Core Cage real apply/release -- gathers live process state, delegates the actual decision to
        // CoreCageService.BuildPlan (pure, unit-tested), then applies/releases through it. Never invoked
        // by any unit test (mutates real process affinity); Task 11 verifies it live.
        // ------------------------------------------------------------------
        /// <summary>Pure guard (review MINOR-2): true when the machine has too few logical cores for
        /// Core Cage to do anything meaningful with. Below this, FeatureFlags' own defensive clamp on
        /// CoreCageReservedCores can bottom out at reservedForGame == totalCores (e.g. a 1-core box),
        /// which would make CoreCageService.BuildPlan throw ArgumentOutOfRangeException and fail the
        /// whole Gaming Mode apply. Checked BEFORE any process enumeration or BuildPlan call.</summary>
        internal static bool ShouldSkipCoreCage(int totalCores) => totalCores <= 2;

        private int ApplyCoreCageReal()
        {
            int totalCores = Environment.ProcessorCount;
            if (ShouldSkipCoreCage(totalCores))
            {
                Logger.Log($"GamingMode: skipping Core Cage -- only {totalCores} logical core(s) (<=2), nowhere meaningful to cage to.");
                return 0;
            }

            int reserved = FeatureFlags.Current.CoreCageReservedCores;
            // Clamp defensively so a stale/bad setting can never throw mid Gaming-Mode apply -- always
            // leave at least one core for the cage.
            if (reserved < 1) reserved = 1;
            if (reserved >= totalCores) reserved = Math.Max(totalCores - 1, 1);

            var whitelist = BuildCoreCageWhitelist();
            var processes = new List<(int Pid, string Name)>();
            int selfPid = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id != selfPid && !ProcessWatcher.IsProtectedSystemProcess(p.ProcessName))
                        processes.Add((p.Id, p.ProcessName));
                }
                catch { /* exited/inaccessible -- skip */ }
                finally { p.Dispose(); }
            }

            CagePlan plan = CoreCageService.BuildPlan(totalCores, reserved, processes, whitelist);
            _lastCagePlan = plan;
            return CoreCageService.Apply(plan);
        }

        private int ReleaseCoreCageReal()
        {
            if (_lastCagePlan == null) return 0;
            int released = CoreCageService.Release(_lastCagePlan);
            _lastCagePlan = null;
            return released;
        }

        /// <summary>Names Core Cage must never confine: the user's own gaming process list (reuses
        /// UserProcessLists rather than inventing a second whitelist), "audiodg" (Windows audio engine --
        /// always protected), the foreground process at cage time, and anything ProcessWatcher's own
        /// classifier currently calls a game (review IMPORTANT-3 -- a game the user never added to their
        /// gaming list was getting caged along with everything else). System/anti-cheat processes are
        /// excluded further upstream via <c>ProcessWatcher.IsProtectedSystemProcess</c> before the
        /// process list even reaches <c>BuildPlan</c>.</summary>
        private static ISet<string> BuildCoreCageWhitelist()
        {
            string? foregroundProcessName = GetForegroundProcessName();

            var runningGameNames = new List<string>();
            foreach (var proc in ProcessWatcher.GetRunningGameProcesses())
            {
                try { runningGameNames.Add(proc.ProcessName); }
                catch { /* exited mid-enumeration -- skip */ }
                finally { proc.Dispose(); }
            }

            return BuildWhitelistSet(UserProcessLists.GetList("gaming"), foregroundProcessName, runningGameNames);
        }

        /// <summary>Pure whitelist-set builder (review IMPORTANT-3): given the user's gaming list, the
        /// foreground process name at cage time (or null), and the names ProcessWatcher currently
        /// classifies as running games, builds the set Core Cage must never confine. No Process/OS
        /// dependency -- unit-tested directly. <see cref="BuildCoreCageWhitelist"/> is the impure
        /// gatherer of these three inputs.</summary>
        internal static ISet<string> BuildWhitelistSet(
            IEnumerable<string>? gamingList,
            string? foregroundProcessName,
            IEnumerable<string>? runningGameProcessNames)
        {
            var whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audiodg" };

            foreach (var name in gamingList ?? Array.Empty<string>())
                whitelist.Add(UserProcessLists.Normalize(name));

            if (!string.IsNullOrEmpty(foregroundProcessName))
                whitelist.Add(UserProcessLists.Normalize(foregroundProcessName));

            foreach (var name in runningGameProcessNames ?? Array.Empty<string>())
                whitelist.Add(UserProcessLists.Normalize(name));

            return whitelist;
        }

        /// <summary>The process name of whatever window is in the foreground right now, or null if it
        /// can't be resolved. User-mode user32 APIs only (GetForegroundWindow / GetWindowThreadProcessId)
        /// -- same EAC-safe pattern already used by SignalCollector/ForegroundWatcher elsewhere in this
        /// codebase. Best-effort: any failure (no foreground window, process exited) degrades to null
        /// rather than throwing, so it can never abort a Gaming Mode apply.</summary>
        private static string? GetForegroundProcessName()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return null;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return null;
                using Process p = Process.GetProcessById((int)pid);
                return p.ProcessName;
            }
            catch
            {
                return null;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // ------------------------------------------------------------------
        // Tweak Ledger -- "what's active + what it earned you" (Task 6). Recording is a plain
        // in-memory Record() per step (benchmark fields null -- "not yet benchmarked" until a Prove It
        // run fills them in); the actual file write is batched to one SaveLedger() call per
        // Apply/Revert so a partial pipeline failure doesn't leave a half-written ledger file.
        // ------------------------------------------------------------------
        private void RecordLedgerEntry(string tweakId) =>
            _ledger.Record(new LedgerEntry(tweakId, DateTimeOffset.Now, true, null, null, null, null));

        private void DeactivateAllLedgerEntries()
        {
            _ledger.Deactivate(TweakIdGamingPipeline);
            _ledger.Deactivate(TweakIdEacPolish);
            _ledger.Deactivate(TweakIdCoreUnpark);
            _ledger.Deactivate(TweakIdCoreCage);
        }

        private void SaveLedger()
        {
            try { _ledger.Save(); }
            catch (Exception ex) { Logger.LogError("GamingMode: ledger save failed", ex); }
        }

        // ------------------------------------------------------------------
        // Persisted IsActive flag -- crash-detectable at next launch.
        // ------------------------------------------------------------------
        private sealed class StateFile
        {
            public bool IsActive { get; set; }
        }

        private StateFile LoadState()
        {
            try
            {
                if (!File.Exists(_statePath)) return new StateFile();
                return JsonConvert.DeserializeObject<StateFile>(File.ReadAllText(_statePath)) ?? new StateFile();
            }
            catch (Exception ex)
            {
                Logger.LogError("GamingMode.LoadState failed", ex);
                return new StateFile();
            }
        }

        /// <summary>internal (not private) so tests can drive the exact save path GamingMode's own
        /// ApplyAsync/RevertAsync use (review MINOR-1 -- a true round-trip through this method, instead
        /// of hand-writing the JSON) without invoking the real, OS-mutating pipeline.</summary>
        internal void SaveState(bool isActive)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_statePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_statePath, JsonConvert.SerializeObject(new StateFile { IsActive = isActive }, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Logger.LogError("GamingMode.SaveState failed", ex);
            }
        }

        private static string DefaultStatePath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoreCage", "mode-state.json");
    }
}
