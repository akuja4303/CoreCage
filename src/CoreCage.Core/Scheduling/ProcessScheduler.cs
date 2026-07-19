using System;
using System.Diagnostics;

namespace CoreCage.Core.Scheduling
{
    /// <summary>
    /// Applies a priority class + CPU-affinity mask to a process (the Process-Lasso-style lever behind
    /// "pin the game to high-performance cores"). Best-effort + logged; never throws. A foundation the
    /// existing ad-hoc pin-to-cores code can migrate onto — affinity math lives in the unit-tested
    /// <see cref="AffinityMask"/>.
    /// </summary>
    public static class ProcessScheduler
    {
        /// <summary>Sets priority + affinity (affinityMask==0 leaves affinity untouched). Returns true on success.</summary>
        public static bool Apply(Process process, ProcessPriorityClass priority, long affinityMask)
        {
            if (process == null) return false;
            try
            {
                process.PriorityClass = priority;
                if (affinityMask != 0) process.ProcessorAffinity = (IntPtr)affinityMask;
                Logger.Event("Scheduled {Name} (pid {Pid}) → priority={Priority} affinity=0x{Mask:X}",
                    process.ProcessName, process.Id, priority, affinityMask);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Scheduling process {SafeName(process)} failed", ex);
                return false;
            }
        }

        /// <summary>Pins every matching running process by name (no ".exe"). Returns the count scheduled.</summary>
        public static int ApplyByName(string processName, ProcessPriorityClass priority, long affinityMask)
        {
            int n = 0;
            try
            {
                string name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? processName[..^4] : processName;
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    using (p) { if (Apply(p, priority, affinityMask)) n++; }
                }
            }
            catch (Exception ex) { Logger.LogError($"ApplyByName '{processName}' failed", ex); }
            return n;
        }

        private static string SafeName(Process p)
        {
            try { return p.ProcessName; } catch { return "<unknown>"; }
        }
    }
}
