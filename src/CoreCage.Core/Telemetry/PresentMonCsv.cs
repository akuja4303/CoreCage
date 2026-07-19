using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CoreCage.Core.Telemetry
{
    /// <summary>
    /// Pure parser for PresentMon CSV output. Pulls a single numeric column (default
    /// <c>MsBetweenPresents</c> — the per-frame frametime) out of a capture, robust to column order
    /// and to PresentMon's "NA"/blank rows. No IO of its own beyond an optional file convenience
    /// wrapper; the heavy lifting (<see cref="ParseColumn(IEnumerable{string}, string)"/>) is testable
    /// against in-memory lines. Feed the result to <see cref="FrametimeStats.FromFrametimes"/>.
    /// </summary>
    public static class PresentMonCsv
    {
        /// <summary>PresentMon column holding the present-to-present interval (frametime) in ms.</summary>
        public const string FrameTimeColumn = "MsBetweenPresents";
        /// <summary>PresentMon column holding the displayed-frame interval (includes generated/FG frames).</summary>
        public const string DisplayedColumn = "MsBetweenDisplayChange";

        /// <summary>
        /// Parses the named numeric column from CSV lines (first line = header). Values that are
        /// missing, "NA", non-numeric, or out of bounds are skipped. Returns an empty list when the
        /// header lacks the column or there are no data rows.
        /// </summary>
        public static List<double> ParseColumn(IEnumerable<string> lines, string column = FrameTimeColumn)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("Column required.", nameof(column));

            var result = new List<double>();
            int colIndex = -1;
            bool headerSeen = false;

            foreach (var raw in lines)
            {
                if (raw == null) continue;
                string line = raw.TrimEnd('\r', '\n');
                if (line.Length == 0) continue;

                if (!headerSeen)
                {
                    headerSeen = true;
                    var header = line.Split(',');
                    for (int i = 0; i < header.Length; i++)
                    {
                        if (string.Equals(header[i].TrimStart('﻿').Trim(), column, StringComparison.OrdinalIgnoreCase))
                        {
                            colIndex = i;
                            break;
                        }
                    }
                    if (colIndex < 0) return result; // column not present → nothing to parse
                    continue;
                }

                var fields = line.Split(',');
                if (colIndex >= fields.Length) continue;

                string cell = fields[colIndex].Trim();
                if (cell.Length == 0 || string.Equals(cell, "NA", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
                    result.Add(value);
            }

            return result;
        }

        /// <summary>Convenience: read frametimes straight from a PresentMon CSV file on disk.</summary>
        public static List<double> ParseFile(string csvPath, string column = FrameTimeColumn)
        {
            if (string.IsNullOrWhiteSpace(csvPath)) throw new ArgumentException("Path required.", nameof(csvPath));
            return ParseColumn(File.ReadLines(csvPath), column);
        }
    }
}
