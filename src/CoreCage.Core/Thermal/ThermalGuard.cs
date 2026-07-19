using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CoreCage.Core.Thermal
{
    /// <summary>
    /// Auto CPU thermal protection. When temp crosses the High threshold it confines the busiest
    /// BACKGROUND processes to a few cores at Idle priority (re-applied each tick so a respawning
    /// swarm — e.g. a transcription/transcode batch — can't keep cooking the chip), then restores
    /// them once temp falls back to Release. NEVER touches the foreground app/game, CoreCage
    /// itself, or core OS processes.
    ///
    /// Why workload-throttling rather than a power cap: ryzenadj faults (0xC0000005) on this Cezanne
    /// APU and powercfg clock caps are ignored by Ryzen boost, so reducing the work is the only lever
    /// that actually drops temperature here.
    ///
    /// Heavy enumeration only happens while hot (engaged or at/above High); cool idle is a no-op.
    /// </summary>
    public sealed class ThermalGuard
    {
        // Never throttle the OS / shell / ourselves. (ProcessName form — no .exe.)
        private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "MemCompression", "smss", "csrss", "wininit", "winlogon",
            "services", "lsass", "svchost", "dwm", "explorer", "fontdrvhost", "CoreCage", "ctfmon",
        };

        private const double HogCpuPct = 8.0;   // only confine genuinely busy processes

        private readonly IntPtr _hotMask;        // few-core mask used while engaged
        private bool _engaged;
        private readonly Dictionary<int, (ProcessPriorityClass prio, IntPtr aff)> _throttled = new();
        private readonly Dictionary<int, (TimeSpan cpu, DateTime at)> _prev = new();

        public bool Engaged => _engaged;
        public int ThrottledCount => _throttled.Count;

        public ThermalGuard()
        {
            int logical = Math.Max(1, Environment.ProcessorCount);
            int cores = Math.Max(2, logical / 6);     // ~1/6 of cores (5600G: 12→2) — strong squeeze
            if (cores > logical) cores = logical;
            long mask = 0;
            for (int i = 0; i < cores; i++) mask |= 1L << i;
            _hotMask = (IntPtr)mask;
        }

        /// <summary>Drive the guard. <paramref name="foregroundPid"/> (and optional
        /// <paramref name="extraProtectedName"/>, e.g. the active game) are never throttled.</summary>
        public void Tick(double tempC, double highC, double releaseC, int foregroundPid, string? extraProtectedName = null)
        {
            switch (ThermalGuardPolicy.Decide(tempC, highC, releaseC, _engaged))
            {
                case ThermalAction.Engage:
                    _engaged = true;
                    Logger.Log($"[ThermalGuard] {tempC:F0}°C ≥ {highC:F0}°C — confining background CPU hogs.");
                    Apply(foregroundPid, extraProtectedName);
                    break;
                case ThermalAction.Sustain:
                    Apply(foregroundPid, extraProtectedName);   // re-apply: catch respawned hogs
                    break;
                case ThermalAction.Release:
                    Logger.Log($"[ThermalGuard] {tempC:F0}°C ≤ {releaseC:F0}°C — releasing {_throttled.Count} process(es).");
                    Restore();
                    _engaged = false;
                    break;
            }
        }

        private void Apply(int foregroundPid, string? extraProtectedName)
        {
            var now = DateTime.UtcNow;
            Process[] procs;
            try { procs = Process.GetProcesses(); } catch { return; }

            foreach (var p in procs)
            {
                try
                {
                    if (p.Id == foregroundPid || p.Id == Environment.ProcessId) continue;
                    string name = p.ProcessName;
                    if (Protected.Contains(name)) continue;
                    if (extraProtectedName != null && name.Equals(extraProtectedName, StringComparison.OrdinalIgnoreCase)) continue;

                    TimeSpan cpu = p.TotalProcessorTime;
                    if (_prev.TryGetValue(p.Id, out var prev))
                    {
                        double secs = (now - prev.at).TotalSeconds;
                        if (secs > 0.05)
                        {
                            double pct = (cpu - prev.cpu).TotalSeconds / secs / Environment.ProcessorCount * 100.0;
                            if (pct >= HogCpuPct)
                            {
                                if (!_throttled.ContainsKey(p.Id))
                                    _throttled[p.Id] = (p.PriorityClass, p.ProcessorAffinity);
                                p.PriorityClass = ProcessPriorityClass.Idle;
                                p.ProcessorAffinity = _hotMask;
                            }
                        }
                    }
                    _prev[p.Id] = (cpu, now);
                }
                catch { /* protected/exited process — skip */ }
            }

            if (_prev.Count > 800) _prev.Clear();   // bound the sample cache
        }

        private void Restore()
        {
            foreach (var kv in _throttled)
            {
                try
                {
                    var p = Process.GetProcessById(kv.Key);
                    p.PriorityClass = kv.Value.prio;
                    p.ProcessorAffinity = kv.Value.aff;
                }
                catch { /* exited — nothing to restore */ }
            }
            _throttled.Clear();
        }

        /// <summary>Restore everything the guard touched (call on app exit / mode change).</summary>
        public void ReleaseNow()
        {
            if (_throttled.Count > 0) Restore();
            _engaged = false;
        }
    }
}
