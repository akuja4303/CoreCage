using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoreCage.Core.GameTune
{
    /// <summary>Flat key/value config adapter parameterised by delimiter and quoting. Covers
    /// Frostbite PROFSAVE (space, unquoted), Stingray/Helldivers .config ('=', unquoted), and
    /// Source .cfg (space, quoted). One code path; per-game specifics live in the profile.</summary>
    public sealed class KeyValueAdapter : IGraphicsConfigAdapter
    {
        private readonly char _delim;
        private readonly bool _quote;
        public string Format { get; }

        public KeyValueAdapter(string format, char delimiter, bool quoteValues)
        {
            Format = format; _delim = delimiter; _quote = quoteValues;
        }

        public GraphicsReadResult Read(string configPath)
        {
            var list = new List<GraphicsSetting>();
            foreach (var raw in File.ReadLines(configPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("#")) continue;
                var idx = line.IndexOf(_delim);
                if (idx <= 0) continue;
                var key = line.Substring(0, idx).Trim();
                var val = line.Substring(idx + 1).Trim().Trim('"');
                list.Add(new GraphicsSetting(key, val));
            }
            return new GraphicsReadResult(list);
        }

        public GraphicsApplyPlan Plan(GraphicsReadResult current, IReadOnlyDictionary<string, string> preset)
        {
            // First-occurrence-wins: real flat key/value config files can repeat a key (e.g. a
            // stray duplicate line), so a plain ToDictionary would throw on the second duplicate.
            // Later duplicates are ignored when computing the current value.
            var cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in current.Settings)
                if (!cur.ContainsKey(s.Key)) cur[s.Key] = s.CurrentValue;
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
                var idx = t.IndexOf(_delim);
                if (idx <= 0 || t.StartsWith("//") || t.StartsWith("#")) continue;
                var key = t.Substring(0, idx).Trim();
                // Only the FIRST matching line for a given key is updated; later duplicate-key
                // lines are left byte-identical.
                if (!written.Contains(key) && toSet.TryGetValue(key, out var val))
                {
                    lines[i] = Line(key, val);
                    written.Add(key);
                }
            }
            foreach (var kv in toSet)
                if (!written.Contains(kv.Key)) lines.Add(Line(kv.Key, kv.Value));

            File.WriteAllLines(configPath, lines);
        }

        private string Line(string key, string val) =>
            _quote ? $"{key}{_delim}\"{val}\"" : $"{key}{_delim}{val}";
    }
}
