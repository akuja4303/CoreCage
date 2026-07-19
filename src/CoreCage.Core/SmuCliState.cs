using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CoreCage.Core
{
    /// <summary>
    /// Pure helpers for driving <c>ryzen-smu-cli.exe</c>: per-core Curve-Optimizer offset clamping,
    /// argument building, and read-back parsing. No I/O, no WPF — fully unit-testable (mirrors
    /// <see cref="TuningState"/>). The CLI offset syntax is <c>--offset core:value,core:value,…</c>.
    /// </summary>
    public static class SmuCliState
    {
        /// <summary>Curve Optimizer is a signed PSM margin; clamp to a conservative, hardware-safe band.</summary>
        public const int CoMin = -30;
        public const int CoMax = 30;

        public static int ClampOffset(int v) => v < CoMin ? CoMin : (v > CoMax ? CoMax : v);

        /// <summary>
        /// Builds the <c>--offset 0:-10,1:-15,…</c> argument from a per-core list (index = core).
        /// Each value is clamped to [CoMin,CoMax]. Throws on a null/empty list so a caller can't
        /// silently issue a no-op write.
        /// </summary>
        public static string BuildOffsetArgs(IReadOnlyList<int> perCore)
        {
            if (perCore == null || perCore.Count == 0)
                throw new ArgumentException("At least one per-core offset is required", nameof(perCore));

            var sb = new System.Text.StringBuilder("--offset ");
            for (int i = 0; i < perCore.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(i.ToString(CultureInfo.InvariantCulture))
                  .Append(':')
                  .Append(ClampOffset(perCore[i]).ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>Builds a per-core offset list with the same value on every core.</summary>
        public static IReadOnlyList<int> UniformOffsets(int offset, int coreCount)
        {
            if (coreCount <= 0) throw new ArgumentOutOfRangeException(nameof(coreCount));
            int clamped = ClampOffset(offset);
            var list = new int[coreCount];
            for (int i = 0; i < coreCount; i++) list[i] = clamped;
            return list;
        }

        /// <summary>
        /// Parses <c>ryzen-smu-cli --get-offsets-terse</c> output — bare comma-separated offsets in core
        /// order with no core identifiers, e.g. "-15,0,2,-20,-10,-25" — into a per-core array. Picks the
        /// first line whose tokens are all integers (skips any banner/log lines). Cores beyond the
        /// returned values stay at int.MinValue ("unknown"). Returns true if a values line was found.
        /// </summary>
        public static bool ParseTerseOffsets(string stdout, int coreCount, out int[] offsets)
        {
            offsets = new int[coreCount];
            for (int i = 0; i < coreCount; i++) offsets[i] = int.MinValue;
            if (string.IsNullOrWhiteSpace(stdout) || coreCount <= 0) return false;

            foreach (string raw in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] toks = raw.Trim().Split(',');
                var vals = new List<int>();
                bool allInts = true;
                foreach (string tk in toks)
                {
                    if (int.TryParse(tk.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                        vals.Add(v);
                    else { allInts = false; break; }
                }
                if (allInts && vals.Count > 0)
                {
                    for (int i = 0; i < vals.Count && i < coreCount; i++) offsets[i] = vals[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>True if every requested core offset equals the read-back value (clamped comparison).</summary>
        public static bool VerifyMatch(IReadOnlyList<int> requested, IReadOnlyList<int> readBack)
        {
            if (requested == null || readBack == null) return false;
            if (requested.Count != readBack.Count) return false;
            for (int i = 0; i < requested.Count; i++)
                if (ClampOffset(requested[i]) != readBack[i]) return false;
            return true;
        }
    }
}
