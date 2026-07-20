namespace CoreCage.Core.Profiles
{
    /// <summary>Which CoreCage mode a game's profile should activate.</summary>
    public enum ProfileMode { None, Gaming }

    /// <summary>A per-game tuning profile: when <see cref="ExeName"/> comes to the foreground,
    /// CoreCage can apply <see cref="Mode"/>. Matched case-insensitively, with/without ".exe".</summary>
    public class GameProfile
    {
        public string ExeName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public ProfileMode Mode { get; set; } = ProfileMode.Gaming;

        /// <summary>Logical CPU core indices to reserve for the game (background/cage work stays off
        /// them). Empty = no per-game override; falls back to the global
        /// FeatureFlags.CoreCageReservedCores default. Runtime-relevant, so it lives on GameProfile
        /// itself (unlike submission-only metadata such as tweaks/notes/benchmark — see
        /// CommunityProfileLoader for those).</summary>
        public int[] ReservedCores { get; set; } = System.Array.Empty<int>();

        /// <summary>Foreground process priority tier, as a <see cref="System.Diagnostics.ProcessPriorityClass"/>
        /// name (e.g. "High", "RealTime", "AboveNormal") — same string convention already used by
        /// SystemTweaks' throttle-snapshot entries.</summary>
        public string Priority { get; set; } = "High";
    }
}
