using System;
using System.Collections.Generic;
using System.IO;

namespace CoreCage.Core.GameTune
{
    /// <summary>Orchestrates a per-game preset apply/restore behind the non-negotiable safety gate:
    /// game must be closed, the target path must be safe, and a backup must succeed before any write.
    /// Every failure returns a typed <see cref="GameTuneResult"/> rather than throwing.</summary>
    public sealed class GameTuneService
    {
        private static readonly IReadOnlyList<GraphicsChange> None = Array.Empty<GraphicsChange>();
        private readonly ConfigBackup _backup;
        private readonly Func<string, bool> _isGameRunning;

        public GameTuneService(ConfigBackup backup, Func<string, bool> isGameRunning)
        {
            _backup = backup;
            _isGameRunning = isGameRunning;
        }

        public GameTuneResult Apply(string gameId, string exeName, GraphicsBlock? graphics)
        {
            if (graphics is null || graphics.GuidedOnly)
                return R(GameTuneStatus.NotSupported, "No auto-apply preset for this game.");
            if (_isGameRunning(exeName))
                return R(GameTuneStatus.GameRunning, "Close the game to apply settings.");

            var path = PathSafety.Expand(graphics.ConfigPath);
            if (!PathSafety.IsSafe(path, graphics.SafeRoots))
                return R(GameTuneStatus.UnsafePath, "Config path is outside the allowed safe roots.");
            if (!File.Exists(path))
                return R(GameTuneStatus.ConfigNotFound, "Launch the game once to generate its config.");

            string backupPath;
            try { backupPath = _backup.Backup(gameId, path); }
            catch (Exception ex) { return R(GameTuneStatus.BackupFailed, "Backup failed: " + ex.Message); }

            try
            {
                var adapter = AdapterRegistry.For(graphics.Format);
                var plan = adapter.Plan(adapter.Read(path), graphics.CompetitivePreset);
                adapter.Write(path, plan);
                return new GameTuneResult(GameTuneStatus.Applied,
                    plan.Changes.Count == 0 ? "Already optimal." : $"Applied {plan.Changes.Count} setting(s).",
                    plan.Changes, backupPath);
            }
            catch (Exception ex)
            {
                return R(GameTuneStatus.ParseError, "Could not apply preset: " + ex.Message);
            }
        }

        public GameTuneResult Restore(string gameId, string exeName, GraphicsBlock? graphics)
        {
            if (graphics is null) return R(GameTuneStatus.NotSupported, "Nothing to restore.");
            if (_isGameRunning(exeName))
                return R(GameTuneStatus.GameRunning, "Close the game to restore settings.");
            var path = PathSafety.Expand(graphics.ConfigPath);
            return _backup.TryRestoreNewest(gameId, path)
                ? R(GameTuneStatus.Restored, "Restored your previous config.")
                : R(GameTuneStatus.ConfigNotFound, "No backup found to restore.");
        }

        private static GameTuneResult R(GameTuneStatus s, string msg) => new(s, msg, None, null);
    }
}
