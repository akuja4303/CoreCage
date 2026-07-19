using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;

namespace CoreCage.Core
{
    public class OptimizerProfile
    {
        public string Name        { get; set; } = "";
        public string Description { get; set; } = "";
        public bool   IsBuiltIn   { get; set; }

        // Power
        public string PowerPlan { get; set; } = "balanced"; // "ultimate" | "high" | "balanced" | "powersaver"

        // Timer
        public bool EnableTimerResolution { get; set; }

        // RAM
        public bool PurgeRamOnApply      { get; set; }
        public int  AutoPurgeThresholdMb { get; set; } = 1536;
        public int  MinFreeRamPercent    { get; set; } = 20;

        // Processes
        public bool ThrottleBackgroundProcesses { get; set; }
        public bool BoostGameProcesses          { get; set; }

        // Network (applied via MainWindow callbacks — keeps network code in one place)
        public bool ApplyGamingNetwork { get; set; }

        // Windows
        public bool DisableTelemetry { get; set; }
        public bool EnableGameMode   { get; set; }
        public bool DisableGameBar   { get; set; }
    }

    public static class ProfileManager
    {
        // Wired by MainWindow at startup — same pattern as ProcessWatcher callbacks
        public static Action? GamingNetworkAction;

        // Full gaming pipeline — wired to GamingModeBtn_Click so the Profiles tab
        // Gaming entry and the Dashboard button do exactly the same thing.
        public static Action? FullGamingModeAction;

        public static string? ActiveProfileName { get; private set; }

        private static readonly string ProfilesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoreCage", "Profiles");

        // ── Built-in profiles ────────────────────────────────────────────────
        public static readonly List<OptimizerProfile> BuiltInProfiles = new List<OptimizerProfile>
        {
            new OptimizerProfile
            {
                Name        = "🎮 Gaming",
                Description = "Max FPS — kills background bloat, realtime CPU priority for games, network tuned for low ping",
                IsBuiltIn   = true,
                PowerPlan   = "ultimate",
                EnableTimerResolution       = true,
                PurgeRamOnApply             = true,
                AutoPurgeThresholdMb        = 512,
                MinFreeRamPercent           = 40,
                ThrottleBackgroundProcesses = true,
                BoostGameProcesses          = true,
                ApplyGamingNetwork          = true,
                DisableTelemetry            = true,
                EnableGameMode              = true,
                DisableGameBar              = true,
            },
            new OptimizerProfile
            {
                Name        = "🎥 Streaming",
                Description = "Smooth game + OBS encode — balanced CPU split, avoids stutter on upload-heavy streams",
                IsBuiltIn   = true,
                PowerPlan   = "high",
                EnableTimerResolution       = true,
                PurgeRamOnApply             = true,
                AutoPurgeThresholdMb        = 2048,
                MinFreeRamPercent           = 25,
                ThrottleBackgroundProcesses = false,
                BoostGameProcesses          = true,
                ApplyGamingNetwork          = false,
                DisableTelemetry            = true,
                EnableGameMode              = true,
                DisableGameBar              = false,
            },
            new OptimizerProfile
            {
                Name        = "⚖️ Balanced",
                Description = "Everyday use — sensible defaults, nothing aggressive, Windows behaves normally",
                IsBuiltIn   = true,
                PowerPlan   = "balanced",
                EnableTimerResolution       = false,
                PurgeRamOnApply             = false,
                AutoPurgeThresholdMb        = 1536,
                MinFreeRamPercent           = 20,
                ThrottleBackgroundProcesses = false,
                BoostGameProcesses          = false,
                DisableTelemetry            = false,
                EnableGameMode              = false,
                DisableGameBar              = false,
            },
            new OptimizerProfile
            {
                Name        = "💤 Idle / Power Saver",
                Description = "Laptop on battery or AFK — minimal CPU activity, lowest power draw",
                IsBuiltIn   = true,
                PowerPlan   = "powersaver",
                EnableTimerResolution       = false,
                PurgeRamOnApply             = false,
                AutoPurgeThresholdMb        = 3072,
                MinFreeRamPercent           = 15,
                ThrottleBackgroundProcesses = false,
                BoostGameProcesses          = false,
                DisableTelemetry            = false,
                EnableGameMode              = false,
                DisableGameBar              = false,
            },
        };

        // ── Custom profile persistence ────────────────────────────────────────
        public static List<OptimizerProfile> LoadCustomProfiles()
        {
            var list = new List<OptimizerProfile>();
            if (!Directory.Exists(ProfilesDir)) return list;
            foreach (var file in Directory.GetFiles(ProfilesDir, "*.json"))
            {
                try
                {
                    var p = JsonConvert.DeserializeObject<OptimizerProfile>(File.ReadAllText(file));
                    if (p != null) { p.IsBuiltIn = false; list.Add(p); }
                }
                catch { }
            }
            return list;
        }

        public static void SaveCustomProfile(OptimizerProfile profile)
        {
            profile.IsBuiltIn = false;
            Directory.CreateDirectory(ProfilesDir);
            string safe = string.Concat(profile.Name.Split(Path.GetInvalidFileNameChars()));
            File.WriteAllText(
                Path.Combine(ProfilesDir, safe + ".json"),
                JsonConvert.SerializeObject(profile, Formatting.Indented));
            Logger.Log($"Profile saved: {profile.Name}");
        }

        public static void DeleteCustomProfile(OptimizerProfile profile)
        {
            string safe = string.Concat(profile.Name.Split(Path.GetInvalidFileNameChars()));
            string path = Path.Combine(ProfilesDir, safe + ".json");
            if (File.Exists(path)) File.Delete(path);
            Logger.Log($"Profile deleted: {profile.Name}");
        }

        // ── Apply ─────────────────────────────────────────────────────────────
        public static void ApplyProfile(OptimizerProfile profile)
        {
            Logger.Log($"--- Applying profile: {profile.Name} ---");
            ActiveProfileName = profile.Name;

            // Built-in Gaming profile → delegate to the full Dashboard pipeline so both
            // entry points (Profiles tab and Gaming Mode button) do identical work.
            if (profile.IsBuiltIn && profile.Name.Contains("Gaming") && FullGamingModeAction != null)
            {
                FullGamingModeAction();
                return;
            }

            // 1. Power plan
            switch (profile.PowerPlan)
            {
                case "ultimate":   SetPowerPlan("e9a42b02-d5df-448d-aa00-03f14749eb61"); break;
                case "high":       SystemTweaks.ApplyHighPerformancePowerPlan(); break;
                case "balanced":   SetPowerPlan("381b4222-f694-41f0-9685-ff5bb260df2e"); break;
                case "powersaver": SetPowerPlan("a1841308-3541-4fab-bc81-f71556f20b4a"); break;
            }

            // 2. Timer resolution
            SystemTweaks.SetTimerResolution(profile.EnableTimerResolution);

            // 3. RAM purge
            if (profile.PurgeRamOnApply)
                MemoryCleaner.PurgeStandbyList();

            // 4. Background processes
            if (profile.ThrottleBackgroundProcesses)
                SystemTweaks.ThrottleBackgroundProcesses();

            // 5. Network tweaks (delegated to MainWindow)
            if (profile.ApplyGamingNetwork)
                GamingNetworkAction?.Invoke();

            // 6. Windows settings
            if (profile.DisableTelemetry) SystemTweaks.DisableTelemetry();
            if (profile.EnableGameMode)   SystemTweaks.EnableGameMode();
            if (profile.DisableGameBar)   SystemTweaks.DisableGameBar();

            Logger.Log($"--- Profile applied: {profile.Name} ---");
        }

        private static void SetPowerPlan(string guid)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName  = "powercfg",
                    Arguments = $"/setactive {guid}",
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
                Logger.Log($"Power plan set: {guid}");
            }
            catch (Exception ex) { Logger.LogError("SetPowerPlan failed", ex); }
        }
    }
}
