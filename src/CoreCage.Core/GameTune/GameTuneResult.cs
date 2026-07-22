using System.Collections.Generic;

namespace CoreCage.Core.GameTune
{
    public enum GameTuneStatus
    {
        Applied, Restored, NotSupported, GameRunning, ConfigNotFound, UnsafePath, BackupFailed, ParseError
    }

    /// <summary>Outcome of a GameTune Apply/Restore: a typed status the UI turns into a card state,
    /// a human message, the diff that was (or would be) written, and the backup path for trust.</summary>
    public sealed record GameTuneResult(
        GameTuneStatus Status,
        string Message,
        IReadOnlyList<GraphicsChange> Changes,
        string? BackupPath);
}
