using System.Collections.Generic;

namespace CoreCage.Core
{
    /// <summary>
    /// The single authoritative list of every registry value CoreCage's Gaming/High-FPS
    /// apply paths mutate. Before the FIRST mutation we snapshot the user's TRUE original values via
    /// <see cref="RegistryBackup"/>; the Big Red Button restores from that snapshot.
    ///
    /// WHY this exists: the apply paths used to write values that nothing reversed (MMCSS, TCP/IP,
    /// Dnscache, the MMCSS Tasks\Games profile, scheduler/power keys). The per-feature "restore"
    /// buttons wrote GUESSED Windows defaults (e.g. HwSchMode=1, DisablePagingExecutive=0) — wrong
    /// for any user whose original differed. Snapshot-before-write captures the real original instead.
    ///
    /// MAINTENANCE: when you add a registry write to an apply path, add its (hive, subKey, name) here
    /// or it will never be reverted. <c>RegistryTweakManifestTests</c> guards the known set.
    /// </summary>
    public static class RegistryTweakManifest
    {
        /// <summary>Snapshot label. Must start with "corecage-" so the Big Red Button's prefix sweep finds it.</summary>
        public const string SnapshotLabel = "corecage-registry-tweaks";

        private const string Mmcss        = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        private const string MmcssGames   = Mmcss + @"\Tasks\Games";
        private const string Tcpip        = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
        private const string Dnscache     = @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters";
        private const string Psched       = @"SOFTWARE\Policies\Microsoft\Windows\Psched";
        private const string PriorityCtl  = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
        private const string PowerThrot   = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
        private const string SessionPower = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
        private const string MemMgmt      = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
        private const string GfxDrivers   = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
        private const string GameConfig   = @"System\GameConfigStore";

        /// <summary>Every (hive, subKey, valueName) an apply path writes. Order is irrelevant to restore.</summary>
        public static readonly IReadOnlyList<(string hive, string subKey, string name)> Targets = new[]
        {
            // ── MMCSS SystemProfile (ApplyRegistryLatencyTweaks / ApplyGamingRegistryTweaks) ──
            ("HKLM", Mmcss, "NetworkThrottlingIndex"),
            ("HKLM", Mmcss, "SystemResponsiveness"),

            // ── MMCSS Tasks\Games profile (ApplyMmcssTasksProfile / ApplyGamingRegistryTweaks / High-FPS) ──
            ("HKLM", MmcssGames, "Affinity"),
            ("HKLM", MmcssGames, "Background Only"),
            ("HKLM", MmcssGames, "Clock Rate"),
            ("HKLM", MmcssGames, "GPU Priority"),
            ("HKLM", MmcssGames, "Priority"),
            ("HKLM", MmcssGames, "Scheduling Category"),
            ("HKLM", MmcssGames, "SFIO Priority"),
            ("HKLM", MmcssGames, "LazyModeEnabled"),

            // ── TCP/IP global parameters (ApplyRegistryLatencyTweaks / ApplyGamingRegistryTweaks) ──
            ("HKLM", Tcpip, "TcpTimedWaitDelay"),
            ("HKLM", Tcpip, "MaxUserPort"),
            ("HKLM", Tcpip, "TcpMaxDataRetransmissions"),
            ("HKLM", Tcpip, "DefaultTTL"),
            ("HKLM", Tcpip, "SackOpts"),
            ("HKLM", Tcpip, "Tcp1323Opts"),
            ("HKLM", Tcpip, "EnablePMTUDiscovery"),
            ("HKLM", Tcpip, "EnableDeadGWDetect"),
            ("HKLM", Tcpip, "EnableConnectionRateLimiting"),

            // ── DNS cache (OptimizeDnsSettings) ──
            ("HKLM", Dnscache, "MaxNegativeCacheTtl"),
            ("HKLM", Dnscache, "MaxCacheTtl"),

            // ── QoS Packet Scheduler (ApplyRegistryLatencyTweaks) ──
            ("HKLM", Psched, "NonBestEffortLimit"),

            // ── Scheduler / interrupt priority (ApplyGamingRegistryTweaks / High-FPS) ──
            ("HKLM", PriorityCtl, "Win32PrioritySeparation"),
            ("HKLM", PriorityCtl, "IRQ8Priority"),

            // ── Power throttling + fast startup (ApplyGamingRegistryTweaks) ──
            ("HKLM", PowerThrot, "PowerThrottlingOff"),
            ("HKLM", SessionPower, "HiberBootEnabled"),

            // ── Kernel paging (High-FPS) ──
            ("HKLM", MemMgmt, "DisablePagingExecutive"),

            // ── Hardware-accelerated GPU scheduling / HAGS (High-FPS) ──
            ("HKLM", GfxDrivers, "HwSchMode"),

            // ── GameDVR fullscreen-exclusive bypass (High-FPS, per-user) ──
            ("HKCU", GameConfig, "GameDVR_DXGIHonorFSEWindowsCompatible"),
            ("HKCU", GameConfig, "GameDVR_FSEBehavior"),
            ("HKCU", GameConfig, "GameDVR_FSEBehaviorMode"),
            ("HKCU", GameConfig, "GameDVR_HonorUserFSEBehaviorMode"),
        };

        /// <summary>
        /// Captures the user's true original values the first time ANY apply path runs. Idempotent:
        /// once a snapshot exists it is never overwritten, so re-running Gaming mode can't
        /// record CoreCage's own values as the "original". Best-effort; never throws.
        /// </summary>
        public static void SnapshotOriginalsOnce()
        {
            if (RegistryBackup.HasSnapshot(SnapshotLabel)) return;
            RegistryBackup.Snapshot(SnapshotLabel, Targets);
        }
    }
}
