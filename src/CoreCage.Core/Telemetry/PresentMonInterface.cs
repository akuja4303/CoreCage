using System;
using System.Diagnostics;
using System.IO;

namespace CoreCage.Core.Telemetry
{
    /// <summary>Outcome of a single PresentMon capture.</summary>
    public sealed class PresentMonResult
    {
        public bool Ran { get; init; }
        public string ProcessName { get; init; } = "";
        public int Seconds { get; init; }
        public FrametimeStats Stats { get; init; } = FrametimeStats.Empty;
        /// <summary>Presented-vs-displayed cadence comparison (Frame Generation detection).</summary>
        public FrameGenAnalysis FrameGen { get; init; } = FrameGenAnalysis.None;
        public string? CsvPath { get; init; }
        public string? Error { get; init; }
        public int FrameCount => Stats.FrameCount;
    }

    /// <summary>
    /// Honest before/after frametime capture via the bundled/installed PresentMon.exe — the rank-2
    /// measurement engine (Council 2026-06-01). ETW-based (no injection → anti-cheat-safe for ARC/EAC),
    /// MIT-licensed tool invoked as a child process so nothing is statically linked. This class is the
    /// thin IO/process shell; all math lives in the pure, unit-tested
    /// <see cref="FrametimeStats"/> / <see cref="PresentMonCsv"/> / <see cref="BenchmarkDelta"/> types.
    ///
    /// Typical use: <c>var before = Capture("PioneerGame-d.exe", 20); /*apply Gaming Mode*/ var after =
    /// Capture(...); BenchmarkDelta.Between(before.Stats, after.Stats).Summary();</c>
    /// Requires the host process to be elevated (PresentMon needs ETW privileges) — CoreCage already is.
    /// </summary>
    public sealed class PresentMonInterface
    {
        private readonly string _exePath;
        private readonly TelemetryHub _hub;

        /// <summary>Locations probed for PresentMon.exe, in order, when no explicit path is given.</summary>
        public static string[] DefaultExeCandidates { get; } = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "PresentMon.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon.exe"),
            @"C:\tools\presentmon\PresentMon.exe",
        };

        public PresentMonInterface(string? exePath = null, TelemetryHub? hub = null)
        {
            _exePath = exePath ?? ResolveExe() ?? DefaultExeCandidates[^1];
            _hub = hub ?? TelemetryHub.Instance;
        }

        /// <summary>The PresentMon.exe path this instance will invoke.</summary>
        public string ExePath => _exePath;

        /// <summary>True when a PresentMon.exe was found on disk.</summary>
        public bool IsAvailable => File.Exists(_exePath);

        /// <summary>Clamp a requested capture length to [1,300]s. An unbounded value (e.g. from
        /// the /benchmark API) would block a thread-pool thread for days and can overflow the ms
        /// timeout into a negative, orphaning the elevated PresentMon child.</summary>
        public static int ClampCaptureSeconds(int seconds) => seconds < 1 ? 1 : (seconds > 300 ? 300 : seconds);

        /// <summary>First existing candidate from <see cref="DefaultExeCandidates"/>, or null.</summary>
        public static string? ResolveExe()
        {
            foreach (var c in DefaultExeCandidates)
                if (File.Exists(c)) return c;
            return null;
        }

        /// <summary>
        /// Captures <paramref name="seconds"/> of frametimes for <paramref name="processName"/>
        /// (e.g. "PioneerGame-d.exe"), computes <see cref="FrametimeStats"/>, and pushes the frametime
        /// samples into the hub's <see cref="TelemetryHub.FrameTime"/> series for the live chart.
        /// Never throws — failures (tool missing, game not running, timeout) come back as
        /// <c>Ran=false</c> with an <see cref="PresentMonResult.Error"/> message.
        /// </summary>
        public PresentMonResult Capture(string processName, int seconds = 20, bool pushToHub = true)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return new PresentMonResult { ProcessName = processName ?? "", Seconds = seconds, Error = "No process name." };
            seconds = ClampCaptureSeconds(seconds);
            if (!IsAvailable)
                return new PresentMonResult { ProcessName = processName, Seconds = seconds, Error = $"PresentMon.exe not found (looked in {string.Join("; ", DefaultExeCandidates)})." };

            string csv = Path.Combine(Path.GetTempPath(), $"rigopt_pm_{Guid.NewGuid():N}.csv");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var a in new[]
                {
                    "--process_name", processName,
                    "--output_file", csv,
                    "--timed", seconds.ToString(),
                    "--terminate_after_timed",
                    "--stop_existing_session",
                    "--no_console_stats",
                })
                {
                    psi.ArgumentList.Add(a);
                }

                using var proc = Process.Start(psi);
                if (proc == null)
                    return new PresentMonResult { ProcessName = processName, Seconds = seconds, Error = "Failed to start PresentMon.exe." };

                // Drain stderr/stdout on background threads so PresentMon can never deadlock on a
                // full pipe, and so we can report its ACTUAL failure reason (not a generic message).
                string stderr = "", stdout = "";
                var errT = System.Threading.Tasks.Task.Run(() => { try { stderr = proc.StandardError.ReadToEnd(); } catch { } });
                var outT = System.Threading.Tasks.Task.Run(() => { try { stdout = proc.StandardOutput.ReadToEnd(); } catch { } });

                // Give the capture its window plus generous headroom for spin-up/flush, then hard-stop.
                int timeoutMs = (seconds + 15) * 1000;
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                    return new PresentMonResult { ProcessName = processName, Seconds = seconds, CsvPath = csv, Error = "PresentMon timed out." };
                }
                System.Threading.Tasks.Task.WaitAll(new[] { errT, outT }, 2000);
                int exitCode = -1; try { exitCode = proc.ExitCode; } catch { }

                if (!File.Exists(csv))
                {
                    string why = (stderr + " " + stdout).Trim();
                    if (string.IsNullOrWhiteSpace(why)) why = "no output — is the game presenting frames, and is the host elevated?";
                    return new PresentMonResult { ProcessName = processName, Seconds = seconds, Error = $"PresentMon exit {exitCode}: {why}" };
                }

                // Read once; pull both the presented (render) cadence and the displayed cadence
                // (the latter includes Frame-Generation frames) so we can flag FG-inflated FPS.
                var lines = File.ReadAllLines(csv);
                // Best-effort temp cleanup: the samples are now in memory, so drop the CSV to bound
                // %TEMP% growth over repeated captures (the /benchmark endpoint can be hit often).
                try { File.Delete(csv); } catch { }
                var frametimes = PresentMonCsv.ParseColumn(lines);
                var displayed  = PresentMonCsv.ParseColumn(lines, PresentMonCsv.DisplayedColumn);
                var stats = FrametimeStats.FromFrametimes(frametimes);
                var frameGen = FrameGenAnalysis.From(frametimes, displayed);

                if (pushToHub)
                    foreach (var ft in frametimes) _hub.Push(TelemetryHub.FrameTime, ft);

                return new PresentMonResult
                {
                    Ran = stats.FrameCount > 0,
                    ProcessName = processName,
                    Seconds = seconds,
                    Stats = stats,
                    FrameGen = frameGen,
                    CsvPath = csv,
                    Error = stats.FrameCount > 0 ? null : "Capture produced 0 valid frames.",
                };
            }
            catch (Exception ex)
            {
                Logger.LogError("PresentMon capture failed", ex);
                return new PresentMonResult { ProcessName = processName, Seconds = seconds, CsvPath = csv, Error = ex.Message };
            }
        }
    }
}
