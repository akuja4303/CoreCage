using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CoreCage.Core
{
    /// <summary>
    /// <see cref="ISmuController"/> backed by <c>ryzen-smu-cli.exe</c> (ZenStates-Core + signed PawnIO),
    /// the path that actually lands Curve-Optimizer writes on Cezanne/SMU-v18 where
    /// <c>ryzenadj --set-coall</c> faults 0xC0000005. Shelling out keeps the GPL tool at the process
    /// boundary so CoreCage's own code stays proprietary.
    ///
    /// Every write checks the process exit code AND reads the offsets back to verify they stuck —
    /// the old ryzenadj path logged false success when the SMU silently rejected the write.
    ///
    /// Flags match ryzen-smu-cli's documented CLI (--offset "c:v,…", --get-offsets-terse). Gated OFF
    /// behind FeatureFlags.NativeCpuCurveOptimizer until the tool + signed PawnIO driver are installed
    /// and a single write is confirmed on hardware (an unvalidated CO value can hard-freeze the rig).
    /// </summary>
    public class RyzenSmuCliController : ISmuController
    {
        // Default install location (mirrors the C:\tools layout used for ryzenadj). Falls back to PATH.
        private static readonly string[] CandidatePaths =
        {
            @"C:\tools\ryzen-smu-cli\ryzen-smu-cli.exe",
            @"C:\tools\ryzen-smu-cli.exe",
        };

        private readonly string? _exe;

        public RyzenSmuCliController(string? explicitPath = null)
        {
            _exe = explicitPath ?? ResolveExe();
        }

        public bool IsAvailable => _exe != null && File.Exists(_exe);

        public SmuApplyResult ApplyAllCoreOffset(int offset, int coreCount)
        {
            if (!IsAvailable)
                return new SmuApplyResult(false, false, -1, "ryzen-smu-cli.exe not found");

            int clamped = SmuCliState.ClampOffset(offset);
            // Use the SMU's native all-core command (--offset-all) rather than per-core --offset.
            // On APUs (5600G/Cezanne, 6-of-8 die with a zero core-disable fuse) the per-core map can't
            // isolate the 6 enabled cores among the 8 slots, but the all-core command applies to exactly
            // the enabled cores. Requires the patched ryzen-smu-cli that exposes --offset-all.
            (int exit, string outp) = Run($"--offset-all {clamped}");
            if (exit != 0)
            {
                string msg = $"SMU all-core CO write FAILED — exit 0x{(uint)exit:X8} ({exit}); offset {clamped} NOT applied. {outp.Trim()}";
                Logger.LogError(msg);
                return new SmuApplyResult(false, false, exit, msg);
            }

            // Read back: every responding core should report the clamped offset (don't trust exit 0 alone).
            IReadOnlyList<int> readBack = ReadPerCoreOffsets(coreCount);
            bool verified = readBack.Count > 0;
            foreach (int v in readBack) if (v != clamped) { verified = false; break; }

            Logger.Event("SMU all-core CO applied ({Offset}); verified={Verified}", clamped, verified);
            return new SmuApplyResult(true, verified, 0,
                verified ? $"CO {clamped} applied + verified on all cores"
                         : $"CO write returned 0 but read-back could not confirm {clamped}");
        }

        public SmuApplyResult ApplyPerCoreOffsets(IReadOnlyList<int> perCoreOffsets)
        {
            if (!IsAvailable)
                return new SmuApplyResult(false, false, -1, "ryzen-smu-cli.exe not found");

            string args = SmuCliState.BuildOffsetArgs(perCoreOffsets);
            (int exit, string outp) = Run(args);

            if (exit != 0)
            {
                string msg = $"SMU CO write FAILED — exit 0x{(uint)exit:X8} ({exit}); offsets NOT applied [{args}]";
                Logger.LogError(msg);
                return new SmuApplyResult(false, false, exit, msg);
            }

            // Read back to confirm the SMU actually accepted the write (don't trust exit 0 alone).
            IReadOnlyList<int> readBack = ReadPerCoreOffsets(perCoreOffsets.Count);
            bool verified = readBack.Count == perCoreOffsets.Count
                            && SmuCliState.VerifyMatch(perCoreOffsets, readBack);

            Logger.Event("SMU CO applied ({Args}); verified={Verified}", args, verified);
            return new SmuApplyResult(true, verified, 0,
                verified ? "CO offsets applied + verified" : "CO write returned 0 but read-back could not confirm");
        }

        public IReadOnlyList<int> ReadPerCoreOffsets(int coreCount)
        {
            if (!IsAvailable || coreCount <= 0) return Array.Empty<int>();
            (int exit, string outp) = Run("--get-offsets-terse");
            if (exit != 0) return Array.Empty<int>();
            return SmuCliState.ParseTerseOffsets(outp, coreCount, out int[] offsets) ? offsets : Array.Empty<int>();
        }

        private (int exit, string output) Run(string args)
        {
            try
            {
                var psi = new ProcessStartInfo(_exe!, args)
                {
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                };
                using Process? p = Process.Start(psi);
                if (p == null) return (-1, "");
                string outp = p.StandardOutput.ReadToEnd();
                string err  = p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                int exit = -1;
                try { if (p.HasExited) exit = p.ExitCode; } catch { }
                return (exit, string.IsNullOrWhiteSpace(err) ? outp : outp + Environment.NewLine + err);
            }
            catch (Exception ex)
            {
                Logger.LogError("ryzen-smu-cli invocation failed", ex);
                return (-1, ex.Message);
            }
        }

        private static string? ResolveExe()
        {
            foreach (string p in CandidatePaths)
                if (File.Exists(p)) return p;
            return null; // not on disk; caller treats as unavailable (PATH resolution left to the OS if wired later)
        }
    }
}
