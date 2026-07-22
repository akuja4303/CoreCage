using System;
using System.Collections.Generic;
using System.Globalization;
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

            var gate = OpenGate(gameId, exeName, graphics, "Close the game to apply settings.");
            if (!gate.Success) return gate.Failure!;

            try
            {
                var adapter = AdapterRegistry.For(graphics.Format);
                var plan = adapter.Plan(adapter.Read(gate.Path), graphics.CompetitivePreset);
                adapter.Write(gate.Path, plan);
                return new GameTuneResult(GameTuneStatus.Applied,
                    plan.Changes.Count == 0 ? "Already optimal." : $"Applied {plan.Changes.Count} setting(s).",
                    plan.Changes, gate.BackupPath);
            }
            catch (Exception ex)
            {
                return R(GameTuneStatus.ParseError, "Could not apply preset: " + ex.Message);
            }
        }

        /// <summary>Syncs a mouse-sensitivity value (already converted by <see cref="SensitivityConverter"/>)
        /// into the game's config, behind the same safety gate as <see cref="Apply"/>.</summary>
        public GameTuneResult ApplySensitivity(string gameId, string exeName, GraphicsBlock graphics,
            SensitivityBlock sens, double computedSens)
        {
            var gate = OpenGate(gameId, exeName, graphics, "Close the game to sync sensitivity.");
            if (!gate.Success) return gate.Failure!;

            try
            {
                var adapter = AdapterRegistry.For(graphics.Format);
                var preset = new Dictionary<string, string>
                {
                    [sens.Key] = computedSens.ToString(CultureInfo.InvariantCulture)
                };
                var plan = adapter.Plan(adapter.Read(gate.Path), preset);
                adapter.Write(gate.Path, plan);
                return new GameTuneResult(GameTuneStatus.Applied,
                    "Synced sensitivity to " + computedSens.ToString(CultureInfo.InvariantCulture) + ".",
                    plan.Changes, gate.BackupPath);
            }
            catch (Exception ex)
            {
                return R(GameTuneStatus.ParseError, "Could not sync sensitivity: " + ex.Message);
            }
        }

        /// <summary>Shared safety gate for <see cref="Apply"/> and <see cref="ApplySensitivity"/>:
        /// game must be closed, the config path must be safe, must exist, and must back up cleanly
        /// before any write is attempted.</summary>
        private Gate OpenGate(string gameId, string exeName, GraphicsBlock graphics, string gameRunningMessage)
        {
            if (_isGameRunning(exeName))
                return Gate.Fail(R(GameTuneStatus.GameRunning, gameRunningMessage));

            var path = PathSafety.Expand(graphics.ConfigPath);
            if (!PathSafety.IsSafe(path, graphics.SafeRoots))
                return Gate.Fail(R(GameTuneStatus.UnsafePath, "Config path is outside the allowed safe roots."));
            if (!File.Exists(path))
                return Gate.Fail(R(GameTuneStatus.ConfigNotFound, "Launch the game once to generate its config."));

            string backupPath;
            try { backupPath = _backup.Backup(gameId, path); }
            catch (Exception ex) { return Gate.Fail(R(GameTuneStatus.BackupFailed, "Backup failed: " + ex.Message)); }

            return Gate.Ok(path, backupPath);
        }

        /// <summary>Outcome of <see cref="OpenGate"/>: either a resolved+backed-up path ready to
        /// write to, or a typed failure result to return as-is.</summary>
        private readonly struct Gate
        {
            public bool Success { get; }
            public GameTuneResult? Failure { get; }
            public string Path { get; }
            public string BackupPath { get; }

            private Gate(bool success, GameTuneResult? failure, string path, string backupPath)
            {
                Success = success;
                Failure = failure;
                Path = path;
                BackupPath = backupPath;
            }

            public static Gate Fail(GameTuneResult failure) => new(false, failure, "", "");
            public static Gate Ok(string path, string backupPath) => new(true, null, path, backupPath);
        }

        public GameTuneResult Restore(string gameId, string exeName, GraphicsBlock? graphics)
        {
            if (graphics is null) return R(GameTuneStatus.NotSupported, "Nothing to restore.");
            if (_isGameRunning(exeName))
                return R(GameTuneStatus.GameRunning, "Close the game to restore settings.");
            var path = PathSafety.Expand(graphics.ConfigPath);
            if (!PathSafety.IsSafe(path, graphics.SafeRoots))
                return R(GameTuneStatus.UnsafePath, "Config path is outside the allowed safe roots.");

            try
            {
                return _backup.TryRestoreNewest(gameId, path)
                    ? R(GameTuneStatus.Restored, "Restored your previous config.")
                    : R(GameTuneStatus.ConfigNotFound, "No backup found to restore.");
            }
            catch (Exception ex)
            {
                return R(GameTuneStatus.ParseError, "Could not restore: " + ex.Message);
            }
        }

        private static GameTuneResult R(GameTuneStatus s, string msg) => new(s, msg, None, null);
    }
}
