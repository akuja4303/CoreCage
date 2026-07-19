using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CoreCage.Core.Modes
{
    /// <summary>
    /// The seam every performance "mode" (Gaming today; Trading/Coding/private modes later) implements.
    /// ModeRegistry drives instances of this uniformly -- neither the registry nor the UI needs to know
    /// anything about a mode's actual tweaks. Exact shape is load-bearing: later tasks and future
    /// private modules depend on it verbatim.
    /// </summary>
    public interface IModeModule
    {
        /// <summary>Stable identifier, e.g. "Gaming". Used as the ModeRegistry.Get key.</summary>
        string Name { get; }

        /// <summary>Human-readable summary of what this mode does, for UI display.</summary>
        string Description { get; }

        /// <summary>True if this mode is currently applied (persisted, so it survives a crash/relaunch).</summary>
        bool IsActive { get; }

        /// <summary>Applies the mode's tweaks. Reports each pipeline step via <paramref name="progress"/> if given.</summary>
        Task<ModeResult> ApplyAsync(IProgress<string>? progress = null);

        /// <summary>Reverts the mode's tweaks. Reports each pipeline step via <paramref name="progress"/> if given.</summary>
        Task<ModeResult> RevertAsync(IProgress<string>? progress = null);
    }

    /// <summary>Outcome of an ApplyAsync/RevertAsync call: whether it succeeded overall, a one-line
    /// summary for UI display, and the ordered list of steps taken (for logs/diagnostics).</summary>
    public sealed record ModeResult(bool Success, string Summary, IReadOnlyList<string> Steps);
}
