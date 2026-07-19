using System;
using System.Management;

namespace CoreCage.Core.Hardware
{
    public enum CpuVendor { Unknown, Amd, Intel }
    public enum GpuVendor { Unknown, Nvidia, Amd, Intel }

    /// <summary>Detected rig identity, so hardware-specific tuning stops assuming the dev's
    /// RTX 3060 / Ryzen 5 5600G. The vendor classifiers are pure + unit-tested; detection reads
    /// CPU/GPU names (via SystemMonitor) and physical core count (WMI) once, cached.</summary>
    public sealed class HardwareProfile
    {
        public string CpuName { get; }
        public CpuVendor CpuVendor { get; }
        public int PhysicalCores { get; }
        public string GpuName { get; }
        public GpuVendor GpuVendor { get; }

        public bool IsRyzen => CpuVendor == CpuVendor.Amd;
        public bool IsNvidiaGpu => GpuVendor == GpuVendor.Nvidia;

        public HardwareProfile(string cpuName, CpuVendor cpuVendor, int physicalCores, string gpuName, GpuVendor gpuVendor)
        {
            CpuName = cpuName; CpuVendor = cpuVendor; PhysicalCores = physicalCores;
            GpuName = gpuName; GpuVendor = gpuVendor;
        }

        // ── pure classifiers (unit-tested) ──────────────────────────────────────
        public static CpuVendor ClassifyCpu(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return CpuVendor.Unknown;
            string n = name.ToLowerInvariant();
            if (n.Contains("amd") || n.Contains("ryzen") || n.Contains("threadripper") || n.Contains("epyc"))
                return CpuVendor.Amd;
            if (n.Contains("intel") || n.Contains("core(tm)") || n.Contains("xeon") ||
                n.Contains("pentium") || n.Contains("celeron"))
                return CpuVendor.Intel;
            return CpuVendor.Unknown;
        }

        public static GpuVendor ClassifyGpu(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return GpuVendor.Unknown;
            string n = name.ToLowerInvariant();
            if (n.Contains("nvidia") || n.Contains("geforce") || n.Contains("rtx") ||
                n.Contains("gtx") || n.Contains("quadro"))
                return GpuVendor.Nvidia;
            if (n.Contains("radeon") || n.Contains("amd ") || n.StartsWith("amd") || n.Contains("rx "))
                return GpuVendor.Amd;
            if (n.Contains("intel") || n.Contains("arc ") || n.Contains("iris") ||
                n.Contains("uhd graphics") || n.Contains("hd graphics"))
                return GpuVendor.Intel;
            return GpuVendor.Unknown;
        }

        /// <summary>Pure: choose the most tuning-relevant GPU from all installed adapter names.
        /// NVIDIA wins outright (its discrete card is what nvidia-smi tunes) so an APU's integrated
        /// Radeon/Intel iGPU can't mask a discrete RTX. Empty input → ("", Unknown).</summary>
        public static (string name, GpuVendor vendor) PickGpu(System.Collections.Generic.IEnumerable<string> names)
        {
            string best = ""; GpuVendor bestV = GpuVendor.Unknown;
            if (names != null)
                foreach (var name in names)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var v = ClassifyGpu(name);
                    if (v == GpuVendor.Nvidia) return (name, v);              // discrete NVIDIA wins
                    if (bestV == GpuVendor.Unknown) { best = name; bestV = v; }
                }
            return (best, bestV);
        }

        // ── detection (IO; cached) ───────────────────────────────────────────────
        private static HardwareProfile? _current;
        public static HardwareProfile Current => _current ??= Detect();

        public static HardwareProfile Detect()
        {
            string cpu = Safe(SystemMonitor.GetCpuName) ?? "";
            var (gpu, gpuVendor) = DetectGpu();
            return new HardwareProfile(cpu, ClassifyCpu(cpu), DetectPhysicalCores(), gpu, gpuVendor);
        }

        /// <summary>Enumerates ALL video controllers (so an APU iGPU + discrete dGPU are both seen),
        /// then prefers NVIDIA via <see cref="PickGpu"/>. Falls back to the single LHM/WMI name.</summary>
        private static (string name, GpuVendor vendor) DetectGpu()
        {
            try
            {
                var names = new System.Collections.Generic.List<string>();
                using var s = new ManagementObjectSearcher("select Name from Win32_VideoController");
                foreach (ManagementObject mo in s.Get())
                {
                    string? n = mo["Name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(n)) names.Add(n!.Trim());
                }
                var picked = PickGpu(names);
                if (picked.vendor != GpuVendor.Unknown || names.Count > 0) return picked;
            }
            catch { /* WMI unavailable */ }
            string fallback = Safe(SystemMonitor.GetGpuName) ?? "";
            return (fallback, ClassifyGpu(fallback));
        }

        private static int DetectPhysicalCores()
        {
            try
            {
                int total = 0;
                using var s = new ManagementObjectSearcher("select NumberOfCores from Win32_Processor");
                foreach (ManagementObject mo in s.Get())
                    total += Convert.ToInt32(mo["NumberOfCores"]);
                if (total > 0) return total;
            }
            catch { /* WMI unavailable */ }
            return Math.Max(1, Environment.ProcessorCount / 2); // SMT-era fallback (5600G: 12/2 = 6)
        }

        private static string? Safe(Func<string?> f) { try { return f(); } catch { return null; } }
    }
}
