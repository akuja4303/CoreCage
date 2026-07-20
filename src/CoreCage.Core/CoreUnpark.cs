using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace CoreCage.Core
{
    /// <summary>
    /// Council pick (democratic vote 2026-06-11 — top score 47, zero vetoes): for Gaming Mode, unpark
    /// every CPU core and raise the minimum processor-performance floor on the ACTIVE power plan; for
    /// Restore / the Big Red Button, restore the user's true originals.
    ///
    /// Cuts core C-state wake latency + context-switch overhead with NO reboot. We deliberately do NOT
    /// touch processor idle-disable / C-state knobs (IDLEDISABLE) — that is the audio-crackle hazard the
    /// council flagged; core-unpark + a min-perf floor alone give the wake-latency win without it.
    ///
    /// All via official <c>powercfg</c> (OS power policy, zero game-process touch) → EAC-safe. Fully
    /// reversible: the first Apply snapshots the real AC indexes to core-unpark-state.json and never
    /// overwrites them, so re-running a mode can't record our own values as the "original".
    /// </summary>
    public static class CoreUnpark
    {
        // Processor power-setting GUIDs (stable across Windows builds; raw GUIDs avoid unregistered-alias failures).
        private const string SubProcessor    = "54533251-82be-4824-96c1-47b60b740d00"; // SUB_PROCESSOR
        private const string CpMinCores      = "0cc5b647-c1df-4637-891a-dec35c318583"; // CPMINCORES   (% cores unparked)
        private const string CpMaxCores      = "ea062031-0e34-4ff1-9b6d-eb1059334028"; // CPMAXCORES
        private const string ProcThrottleMin = "893dee8e-2bef-41e0-89c6-b55d0929964c"; // PROCTHROTTLEMIN (min perf %)

        private const int UnparkAll = 100;  // CPMINCORES=100 → every core unparked
        private const int MaxCores  = 100;  // CPMAXCORES=100
        private const int PerfFloor = 100;  // PROCTHROTTLEMIN=100 → no down-throttle while gaming

        // Deliberately NOT prefixed "corecage-": that prefix is swept by RegistryBackup.RestoreAllWithPrefix,
        // which would try to parse this (different schema) as a registry snapshot. RestoreEverything calls
        // CoreUnpark.RestoreAll() explicitly instead.
        private const string StateFileName = "core-unpark-state.json";

        private static string StatePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoreCage", "Backups", StateFileName);

        /// <summary>Original AC power-setting indexes captured before the first Apply.</summary>
        public sealed class State
        {
            public int? CpMinCoresAc { get; set; }
            public int? CpMaxCoresAc { get; set; }
            public int? ProcThrottleMinAc { get; set; }
        }

        /// <summary>Unparks all cores + sets the perf floor on the active scheme. Snapshots originals once. Never throws.</summary>
        public static void ApplyAll()
        {
            try
            {
                SnapshotOriginalsOnce();
                foreach (string args in BuildApplyArgs())
                    RunPowercfg(args);
                RunPowercfg("/setactive scheme_current");   // applies without reboot
                Logger.Log("CoreUnpark: all cores unparked + min-perf floor 100% on the active plan (no reboot)");
            }
            catch (Exception ex) { Logger.LogError("CoreUnpark.ApplyAll failed", ex); }
        }

        /// <summary>Restores the user's original core-park + perf-floor indexes captured at first Apply.
        /// Returns true if a snapshot existed and was applied. Never throws.</summary>
        public static bool RestoreAll()
        {
            try
            {
                State? s = LoadState();
                if (s == null) { Logger.Log("CoreUnpark: nothing to restore (never applied)"); return false; }

                if (s.CpMinCoresAc.HasValue)      RunPowercfg(SetAc(CpMinCores, s.CpMinCoresAc.Value));
                if (s.CpMaxCoresAc.HasValue)      RunPowercfg(SetAc(CpMaxCores, s.CpMaxCoresAc.Value));
                if (s.ProcThrottleMinAc.HasValue) RunPowercfg(SetAc(ProcThrottleMin, s.ProcThrottleMinAc.Value));
                RunPowercfg("/setactive scheme_current");
                Logger.Log("CoreUnpark: original core-park + perf-floor restored");
                return true;
            }
            catch (Exception ex) { Logger.LogError("CoreUnpark.RestoreAll failed", ex); return false; }
        }

        /// <summary>The powercfg arg strings the Apply writes (AC + DC for all three settings). Pure — unit-tested.</summary>
        public static IReadOnlyList<string> BuildApplyArgs() => new[]
        {
            SetAc(CpMinCores, UnparkAll),      SetDc(CpMinCores, UnparkAll),
            SetAc(CpMaxCores, MaxCores),       SetDc(CpMaxCores, MaxCores),
            SetAc(ProcThrottleMin, PerfFloor), SetDc(ProcThrottleMin, PerfFloor),
        };

        private static string SetAc(string setting, int value) =>
            $"/setacvalueindex scheme_current {SubProcessor} {setting} {value}";
        private static string SetDc(string setting, int value) =>
            $"/setdcvalueindex scheme_current {SubProcessor} {setting} {value}";

        private static void SnapshotOriginalsOnce()
        {
            if (File.Exists(StatePath)) return;   // first-apply only — never capture our own applied values
            string query = RunPowercfgCapture($"/query scheme_current {SubProcessor}");
            var s = new State
            {
                CpMinCoresAc      = ParseAcIndex(query, CpMinCores),
                CpMaxCoresAc      = ParseAcIndex(query, CpMaxCores),
                ProcThrottleMinAc = ParseAcIndex(query, ProcThrottleMin),
            };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
                File.WriteAllText(StatePath, JsonConvert.SerializeObject(s, Formatting.Indented));
                Logger.Log($"CoreUnpark: snapshotted originals " +
                           $"(CPMINCORES={s.CpMinCoresAc?.ToString() ?? "?"}, CPMAXCORES={s.CpMaxCoresAc?.ToString() ?? "?"}, " +
                           $"PROCTHROTTLEMIN={s.ProcThrottleMinAc?.ToString() ?? "?"})");
            }
            catch (Exception ex) { Logger.LogError("CoreUnpark snapshot save failed", ex); }
        }

        private static State? LoadState()
        {
            try { return File.Exists(StatePath) ? JsonConvert.DeserializeObject<State>(File.ReadAllText(StatePath)) : null; }
            catch (Exception ex) { Logger.LogError("CoreUnpark.LoadState failed", ex); return null; }
        }

        /// <summary>Parses the "Current AC Power Setting Index" that follows <paramref name="settingGuid"/>
        /// in <c>powercfg /query</c> output, returning the decimal value (or null if absent). Pure — unit-tested.</summary>
        public static int? ParseAcIndex(string queryOutput, string settingGuid)
        {
            if (string.IsNullOrEmpty(queryOutput) || string.IsNullOrEmpty(settingGuid)) return null;
            int g = queryOutput.IndexOf(settingGuid, StringComparison.OrdinalIgnoreCase);
            if (g < 0) return null;
            Match m = Regex.Match(queryOutput.Substring(g), @"Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
                return v;
            return null;
        }

        // ── process plumbing ──
        private static void RunPowercfg(string args)
        {
            try
            {
                using var p = Process.Start(MakePsi(args));
                if (p == null) { Logger.Log($"CoreUnpark: powercfg failed to start ({args})"); return; }
                p.WaitForExit(5000);
                if (p.ExitCode != 0)
                    Logger.Log($"CoreUnpark: powercfg {args} exit {p.ExitCode}: {p.StandardError.ReadToEnd().Trim()}");
            }
            catch (Exception ex) { Logger.LogError($"CoreUnpark.RunPowercfg({args})", ex); }
        }

        private static string RunPowercfgCapture(string args)
        {
            try
            {
                using var p = Process.Start(MakePsi(args));
                if (p == null) return "";
                string outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return outp;
            }
            catch (Exception ex) { Logger.LogError($"CoreUnpark.RunPowercfgCapture({args})", ex); return ""; }
        }

        private static ProcessStartInfo MakePsi(string args) => new ProcessStartInfo
        {
            FileName = "powercfg.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
    }
}
