using CoreCage.Core.GameTune;

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

        /// <summary>Logical CPU core indices this profile wants reserved for the game (background/cage
        /// work kept off them). Captured and validated today, but <b>not yet read by anything at
        /// runtime</b> — the live reserved-cores behavior is still the global
        /// FeatureFlags.CoreCageReservedCores setting, regardless of what a per-profile value says
        /// here. This field exists so submissions can carry the right value now; it starts taking
        /// effect once per-profile application is wired up (a future step), at which point it will
        /// override the global default the way this doc used to (incorrectly) claim it already does.
        /// Runtime-relevant by design, so it lives on GameProfile itself (unlike submission-only
        /// metadata such as tweaks/notes/benchmark — see CommunityProfileLoader for those).</summary>
        public int[] ReservedCores { get; set; } = System.Array.Empty<int>();

        /// <summary>Foreground process priority tier this profile wants, as a
        /// <see cref="System.Diagnostics.ProcessPriorityClass"/> name (e.g. "High", "RealTime",
        /// "AboveNormal") — same string convention already used by SystemTweaks' throttle-snapshot
        /// entries. Captured and validated today, but <b>not yet applied by anything at runtime</b>;
        /// like <see cref="ReservedCores"/>, it takes effect once per-profile application is wired up.</summary>
        public string Priority { get; set; } = "High";

        /// <summary>Optional in-game graphics-settings context. Null when the game has no curated
        /// preset (unknown game, or Unity title flagged guided-only). Runtime-relevant → lives on
        /// GameProfile, loaded by CommunityProfileLoader.</summary>
        public GraphicsBlock? Graphics { get; set; }
    }
}
