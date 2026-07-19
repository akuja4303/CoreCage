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
    }
}
