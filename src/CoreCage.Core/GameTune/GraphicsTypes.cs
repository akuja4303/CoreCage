using System.Collections.Generic;

namespace CoreCage.Core.GameTune
{
    /// <summary>The in-game graphics-settings context for one game: where its config lives, its
    /// format, the safe directories the config must sit under, and the max-FPS values to write.</summary>
    public sealed record GraphicsBlock(
        string Format,
        string ConfigPath,
        IReadOnlyList<string> SafeRoots,
        IReadOnlyDictionary<string, string> CompetitivePreset,
        bool GuidedOnly,
        string? PostApplyNotes);

    /// <summary>One setting's current value as read from a config file (null = absent).</summary>
    public sealed record GraphicsSetting(string Key, string? CurrentValue);

    /// <summary>All settings an adapter could read back from a config file.</summary>
    public sealed record GraphicsReadResult(IReadOnlyList<GraphicsSetting> Settings);

    /// <summary>One setting change the apply step will make.</summary>
    public sealed record GraphicsChange(string Key, string? From, string To);

    /// <summary>The diff between current config and the target preset — what Write will apply.</summary>
    public sealed record GraphicsApplyPlan(IReadOnlyList<GraphicsChange> Changes);

    /// <summary>Per-game mouse-sensitivity context: the config key holding sensitivity, and the
    /// game's yaw coefficient (degrees turned per count.sens-unit) used to convert an equivalent-feel
    /// value across games. Rides on the game's graphics block for config path/format/safe-roots.</summary>
    public sealed record SensitivityBlock(string Key, double Yaw);
}
