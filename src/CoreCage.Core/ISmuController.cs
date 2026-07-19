using System.Collections.Generic;

namespace CoreCage.Core
{
    /// <summary>Outcome of an SMU Curve-Optimizer write attempt.</summary>
    public readonly struct SmuApplyResult
    {
        /// <summary>The CLI process exited 0.</summary>
        public bool Ok { get; }
        /// <summary>The read-back matched the requested offsets (only meaningful when the CLI supports reads).</summary>
        public bool Verified { get; }
        public int ExitCode { get; }
        public string Message { get; }

        public SmuApplyResult(bool ok, bool verified, int exitCode, string message)
        {
            Ok = ok; Verified = verified; ExitCode = exitCode; Message = message;
        }
    }

    /// <summary>
    /// CPU SMU control seam for AMD Curve Optimizer (per-core PSM margin). This is the replacement
    /// path for the crashing <c>ryzenadj --set-coall</c> on Cezanne/SMU-v18 — see docs/UPGRADES.md
    /// TIER 0. The shipping implementation (<see cref="RyzenSmuCliController"/>) shells out to
    /// <c>ryzen-smu-cli.exe</c> (which drives ZenStates-Core over the signed PawnIO driver), keeping
    /// the GPL dependency at the process boundary.
    ///
    /// ⚠️ Applying CO offsets can hard-freeze the rig if a value is unstable. Wiring this into presets
    /// or an auto-tune scan is SUPERVISED-ONLY and must follow on-hardware validation.
    /// </summary>
    public interface ISmuController
    {
        /// <summary>True if the underlying tool + driver are present.</summary>
        bool IsAvailable { get; }

        /// <summary>Applies a Curve-Optimizer offset per physical core (index = core). Values are clamped.</summary>
        SmuApplyResult ApplyPerCoreOffsets(IReadOnlyList<int> perCoreOffsets);

        /// <summary>Convenience: applies the same offset to every core.</summary>
        SmuApplyResult ApplyAllCoreOffset(int offset, int coreCount);

        /// <summary>Reads back current per-core offsets if the tool supports it; empty list otherwise.</summary>
        IReadOnlyList<int> ReadPerCoreOffsets(int coreCount);
    }
}
