using System;
using System.Collections.Generic;
using System.IO;

namespace CoreCage.Core
{
    /// <summary>
    /// App-exit restore plumbing, factored out of <c>App.OnExit</c> so it is unit-testable. Runs each
    /// shutdown restore step catching failures, and persists any failures to <c>last-exit-errors.txt</c>
    /// — INDEPENDENT of the Logger (which is torn down immediately after exit). This closes the
    /// last hole in the restore-honesty theme: a failed shutdown-restore is never silently swallowed by
    /// the logger's teardown — it lands in a standalone file the owner can read.
    /// </summary>
    public static class ExitRestore
    {
        /// <summary>Runs each (label, action) step, swallowing exceptions but recording "label: message"
        /// for any that throw. Returns the failure list (empty = all clean). Never throws.</summary>
        public static List<string> RunRestoreSteps(IEnumerable<(string label, Action action)> steps)
        {
            var failures = new List<string>();
            if (steps == null) return failures;
            foreach (var (label, action) in steps)
            {
                try { action?.Invoke(); }
                catch (Exception ex) { failures.Add($"{label}: {ex.Message}"); }
            }
            return failures;
        }

        /// <summary>Writes the failures to &lt;logDir&gt;\last-exit-errors.txt. Returns true iff a file was
        /// written (i.e. there were failures). No-op for an empty/null list. Best-effort; never throws.</summary>
        public static bool WriteExitErrors(IReadOnlyList<string> failures, string logDir)
        {
            if (failures == null || failures.Count == 0) return false;
            try
            {
                Directory.CreateDirectory(logDir);
                string path = Path.Combine(logDir, "last-exit-errors.txt");
                File.WriteAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {failures.Count} restore step(s) failed on exit:" +
                    Environment.NewLine + string.Join(Environment.NewLine, failures) + Environment.NewLine);
                return true;
            }
            catch { return false; }
        }

        /// <summary>The standard log directory last-exit-errors.txt lives in (next to the rolling logs).</summary>
        public static string DefaultLogDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoreCage", "Logs");
    }
}
