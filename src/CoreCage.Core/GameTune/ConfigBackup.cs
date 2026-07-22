using System;
using System.IO;
using System.Linq;

namespace CoreCage.Core.GameTune
{
    /// <summary>Copies a game's config file to a timestamped backup before GameTune writes it, and
    /// restores the newest backup on demand. No write is ever performed without a backup succeeding.</summary>
    public sealed class ConfigBackup
    {
        private readonly string _backupRoot;
        public ConfigBackup(string backupRoot) => _backupRoot = backupRoot;

        public string Backup(string gameId, string configPath)
        {
            var stamp = DateTime.UtcNow.Ticks.ToString();
            var dir = Path.Combine(_backupRoot, Sanitize(gameId), stamp);
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, Path.GetFileName(configPath));
            File.Copy(configPath, dest, overwrite: true);
            return dest;
        }

        public bool TryRestoreNewest(string gameId, string configPath)
        {
            var gameDir = Path.Combine(_backupRoot, Sanitize(gameId));
            if (!Directory.Exists(gameDir)) return false;
            var newest = Directory.GetDirectories(gameDir)
                .OrderByDescending(d => d)
                .Select(d => Path.Combine(d, Path.GetFileName(configPath)))
                .FirstOrDefault(File.Exists);
            if (newest == null) return false;
            File.Copy(newest, configPath, overwrite: true);
            return true;
        }

        private static string Sanitize(string id) =>
            string.Concat(id.Split(Path.GetInvalidFileNameChars()));
    }
}
