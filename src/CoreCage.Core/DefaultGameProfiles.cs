using System.Collections.Generic;

namespace CoreCage.Core
{
    /// <summary>
    /// Curated default game profiles bundled with CoreCage. Ten popular competitive
    /// titles, each pre-tuned: foreground priority, QoS DSCP for the title's UDP traffic,
    /// IFEO suggestions, and notes on title-specific quirks.
    ///
    /// User adds them to their gaming list in one click via Settings page. ProfileMatcher
    /// auto-triggers when these processes come to foreground (if AutoApplyGameProfiles is on).
    ///
    /// All curated from actual executable names + standard competitive-FPS tuning. Add new
    /// titles by appending to All. Keep the list to titles that are popular AND benefit
    /// measurably from optimization -- not every game needs a profile.
    /// </summary>
    public static class DefaultGameProfiles
    {
        public static IReadOnlyList<GameProfileTemplate> All { get; } = new List<GameProfileTemplate>
        {
            // ===== EAC-protected =====
            new GameProfileTemplate
            {
                DisplayName = "Arc Raiders",
                Publisher = "Embark Studios",
                AntiCheat = "EAC",
                ExecutableNames = new[] { "PioneerGame-Win64-Shipping.exe", "PioneerGame-d.exe" },
                QosDscp = 48,                   // CS6 -- highest priority class on most consumer routers
                IsCompetitive = true,
                EacSafe = true,
                Notes = "UE5; TdrDelay 10s recommended (shader compile spikes >2s). VSync OFF + FSR FG re-toggle per launch."
            },
            new GameProfileTemplate
            {
                DisplayName = "Apex Legends",
                Publisher = "Respawn / EA",
                AntiCheat = "EAC",
                ExecutableNames = new[] { "r5apex.exe", "r5apex_dx12.exe" },
                QosDscp = 46,                   // EF -- Expedited Forwarding
                IsCompetitive = true,
                EacSafe = true,
                Notes = "Source engine; +fps_max 0 in autoexec, +mat_disable_d3d9ex 1 for older HW."
            },
            new GameProfileTemplate
            {
                DisplayName = "Fortnite",
                Publisher = "Epic Games",
                AntiCheat = "EAC",
                ExecutableNames = new[] { "FortniteClient-Win64-Shipping.exe" },
                QosDscp = 46,
                IsCompetitive = true,
                EacSafe = true,
                Notes = "UE5; Performance Mode preset cuts CPU/GPU load 30%; Hardware Ray Tracing OFF in competitive."
            },
            new GameProfileTemplate
            {
                DisplayName = "Marvel Rivals",
                Publisher = "NetEase",
                AntiCheat = "EAC",
                ExecutableNames = new[] { "Marvel-Win64-Shipping.exe" },
                QosDscp = 46,
                IsCompetitive = true,
                EacSafe = true,
                Notes = "UE5; benefits heavily from MSI mode for GPU (high-throughput effects)."
            },
            new GameProfileTemplate
            {
                DisplayName = "Rust",
                Publisher = "Facepunch",
                AntiCheat = "EAC",
                ExecutableNames = new[] { "RustClient.exe" },
                QosDscp = 46,
                IsCompetitive = true,
                EacSafe = true,
                Notes = "Unity; allocate as much VRAM as possible; GraphicsQuality 5+ for performance."
            },

            new GameProfileTemplate
            {
                DisplayName = "Battlefield 6",
                Publisher = "DICE / EA",
                AntiCheat = "EA Javelin",
                ExecutableNames = new[] { "bf6.exe" },
                QosDscp = 46,
                IsCompetitive = true,
                EacSafe = true,
                Notes = "Frostbite; CPU-bound on 6-core parts — the background cage is the measured win here (5600G: CPU 100%/GPU 46% → CPU 61%/GPU 94%). Javelin is kernel-level; IFEO-only boosts."
            },

            // ===== BattlEye-protected =====
            new GameProfileTemplate
            {
                DisplayName = "Rainbow Six Siege",
                Publisher = "Ubisoft",
                AntiCheat = "BattlEye",
                ExecutableNames = new[] { "RainbowSix.exe", "RainbowSix_Vulkan.exe" },
                QosDscp = 46,
                IsCompetitive = true,
                EacSafe = false,
                Notes = "Vulkan client preferred for lower CPU overhead. AnvilNext; +RenderAheadLimit 1."
            },
            new GameProfileTemplate
            {
                DisplayName = "Escape From Tarkov",
                Publisher = "Battlestate Games",
                AntiCheat = "BattlEye",
                ExecutableNames = new[] { "EscapeFromTarkov.exe" },
                QosDscp = 46,
                IsCompetitive = true,
                EacSafe = false,
                Notes = "Unity; very RAM-bound; close everything browser-related before raid. 8+ TX/RX buffers help."
            },

            // ===== Vanguard / kernel-level =====
            new GameProfileTemplate
            {
                DisplayName = "Valorant",
                Publisher = "Riot",
                AntiCheat = "Vanguard (kernel)",
                ExecutableNames = new[] { "VALORANT-Win64-Shipping.exe", "VALORANT.exe" },
                QosDscp = 46,
                IsCompetitive = true,
                EacSafe = true,
                Notes = "UE4; vanguard runs at kernel level continuously -- IFEO priority is the only safe boost."
            },
            new GameProfileTemplate
            {
                DisplayName = "League of Legends",
                Publisher = "Riot",
                AntiCheat = "Vanguard (kernel)",
                ExecutableNames = new[] { "League of Legends.exe" },
                QosDscp = 34,                   // AF41
                IsCompetitive = true,
                EacSafe = true,
                Notes = "Older engine; cap to monitor refresh; vsync OFF."
            },

            // ===== VAC / open source =====
            new GameProfileTemplate
            {
                DisplayName = "Counter-Strike 2",
                Publisher = "Valve",
                AntiCheat = "VAC",
                ExecutableNames = new[] { "cs2.exe" },
                QosDscp = 46,
                IsCompetitive = true,
                EacSafe = true,
                Notes = "Source 2; -nojoy -allow_third_party_software in launch options for max FPS."
            },
            new GameProfileTemplate
            {
                DisplayName = "Overwatch 2",
                Publisher = "Blizzard",
                AntiCheat = "Warden",
                ExecutableNames = new[] { "Overwatch.exe" },
                QosDscp = 46,
                IsCompetitive = true,
                EacSafe = true,
                Notes = "Display Performance Stats > Net Graph in client; ping should be flat post-Gaming Mode."
            },
            new GameProfileTemplate
            {
                DisplayName = "Call of Duty: Warzone",
                Publisher = "Activision",
                AntiCheat = "RICOCHET",
                ExecutableNames = new[] { "cod.exe", "ModernWarfare.exe" },
                QosDscp = 46,
                IsCompetitive = true,
                EacSafe = true,
                Notes = "DirectStorage helps; very NIC-buffer-sensitive. Use Gaming Mode++ NIC hardening."
            },
        };

        /// <summary>Get a profile by display name (case-insensitive). Null if not found.</summary>
        public static GameProfileTemplate? FindByDisplayName(string name)
        {
            foreach (var p in All)
                if (string.Equals(p.DisplayName, name, System.StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        /// <summary>Get a profile by executable name (case-insensitive). Null if not found.</summary>
        public static GameProfileTemplate? FindByExe(string exeName)
        {
            foreach (var p in All)
                foreach (var exe in p.ExecutableNames)
                    if (string.Equals(exe, exeName, System.StringComparison.OrdinalIgnoreCase))
                        return p;
            return null;
        }
    }

    public class GameProfileTemplate
    {
        public string DisplayName { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string AntiCheat { get; set; } = "";
        public string[] ExecutableNames { get; set; } = System.Array.Empty<string>();
        public int QosDscp { get; set; } = 46;        // EF default
        public bool IsCompetitive { get; set; }
        /// <summary>True if our EAC-safe polish layer is safe to apply to this title.</summary>
        public bool EacSafe { get; set; } = true;
        public string Notes { get; set; } = "";
    }
}
