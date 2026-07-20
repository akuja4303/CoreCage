namespace CoreCage.App;

/// <summary>
/// Three-state classification for a group VM's status bar (review IMPORTANT-1). Before this, every
/// status bar rendered off a bool <c>LastOk</c>, so idle/default and purely-informational messages
/// ("Pick a mode.", "No processes found — this can happen if CoreCage isn't running elevated.",
/// "No saved profiles yet.") had no way to say "nothing happened, this is just the current state" —
/// they fell through to <c>LastOk == true</c> and rendered as a green success checkmark on cold
/// launch, which is not honest (there was no success to report).
///
/// <see cref="Neutral"/> = idle/default/cold-launch state, or a purely informational message (an
/// empty-state explainer, a live readout). No ✓, no ✗ — a muted brush (TextLo) and a neutral glyph.
/// <see cref="Success"/> = a completed action actually succeeded. Green ✓.
/// <see cref="Error"/>   = a completed action actually failed. Red ✗ — conveyed by glyph + text, not
/// color alone, so it still reads for colorblind users.
///
/// <c>LastOk</c> is kept on each VM (other code / tests depend on it as the raw ok/fail of the last
/// action), but the status bar's brush+glyph must be driven by this enum, not the bool.
/// </summary>
public enum StatusKind
{
    Neutral,
    Success,
    Error,
}
