using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoreCage.Core.GameTune
{
    /// <summary>Unreal Engine GameUserSettings.ini (key=value under [Section] headers). Covers
    /// ARC Raiders and Dead by Daylight. Matches keys case-insensitively, ignoring section, and
    /// rewrites values in-place so unrelated keys and comments survive untouched.</summary>
    public sealed class UnrealIniAdapter : IGraphicsConfigAdapter
    {
        public string Format => "unreal-ini";

        public GraphicsReadResult Read(string configPath)
        {
            var list = new List<GraphicsSetting>();
            foreach (var raw in File.ReadLines(configPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("[")) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                list.Add(new GraphicsSetting(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
            }
            return new GraphicsReadResult(list);
        }

        public GraphicsApplyPlan Plan(GraphicsReadResult current, IReadOnlyDictionary<string, string> preset)
        {
            var cur = current.Settings.ToDictionary(s => s.Key, s => s.CurrentValue, StringComparer.OrdinalIgnoreCase);
            var changes = new List<GraphicsChange>();
            foreach (var kv in preset)
            {
                cur.TryGetValue(kv.Key, out var existing);
                if (!string.Equals(existing, kv.Value, StringComparison.OrdinalIgnoreCase))
                    changes.Add(new GraphicsChange(kv.Key, existing, kv.Value));
            }
            return new GraphicsApplyPlan(changes);
        }

        public void Write(string configPath, GraphicsApplyPlan plan)
        {
            if (plan.Changes.Count == 0) return;
            var toSet = plan.Changes.ToDictionary(c => c.Key, c => c.To, StringComparer.OrdinalIgnoreCase);
            var lines = File.ReadAllLines(configPath).ToList();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                var eq = t.IndexOf('=');
                if (eq <= 0 || t.StartsWith(";") || t.StartsWith("[")) continue;
                var key = t.Substring(0, eq).Trim();
                if (toSet.TryGetValue(key, out var val)) { lines[i] = $"{key}={val}"; written.Add(key); }
            }
            // Append any preset key that had no existing line, under the last section (or file end).
            foreach (var kv in toSet)
                if (!written.Contains(kv.Key)) lines.Add($"{kv.Key}={kv.Value}");

            File.WriteAllLines(configPath, lines);
        }
    }
}
