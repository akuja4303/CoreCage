using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace CoreCage.Core.Modes
{
    /// <summary>
    /// Built-in "Gaming" IModeModule. Wraps the existing engine pipeline -- Gaming Mode++ (MSI/NIC/
    /// GameDVR/UWP/QoS), EAC-safe IFEO+powercfg+FSO polish for the user's gaming process list, and
    /// CoreUnpark (core-unpark + perf floor) -- behind the uniform Apply/Revert seam.
    ///
    /// Apply runs the pipeline on a background thread and reports each step via IProgress&lt;string&gt;.
    /// Revert reverses the same three layers in the opposite order; if any revert step throws, it falls
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

                    progress?.Report("EAC-safe polish applying to gaming process list...");
                    int polished = _applyPolish(_gamingProcessList());
                    steps.Add($"EAC-safe polish applied ({polished} process(es))");

                    progress?.Report("Core-unpark + perf-floor applying...");
                    _applyCoreUnpark();
                    steps.Add("Core-unpark applied");

                    SaveState(true);
                    progress?.Report("Gaming Mode applied.");
                    return new ModeResult(true, "Gaming Mode applied", steps);
                }
                catch (Exception ex)
                {
                    Logger.LogError("GamingMode.ApplyAsync failed", ex);
                    steps.Add("FAILED: " + ex.Message);
                    return new ModeResult(false, "Gaming Mode apply failed: " + ex.Message, steps);
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
                    progress?.Report("EAC-safe polish reverting...");
                    int restored = _restorePolish(_gamingProcessList());
                    steps.Add($"EAC-safe polish restored ({restored} process(es))");

                    progress?.Report("Gaming Mode++ reverting...");
                    _restoreGamingModePlusPlus();
                    steps.Add("Gaming Mode++ reverted");

                    progress?.Report("Core-unpark reverting...");
                    bool coreUnparkRestored = _restoreCoreUnpark();
                    steps.Add(coreUnparkRestored ? "Core-unpark restored" : "Core-unpark: nothing to restore");

                    SaveState(false);
                    progress?.Report("Gaming Mode reverted.");
                    return new ModeResult(true, "Gaming Mode reverted", steps);
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

        private void SaveState(bool isActive)
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
