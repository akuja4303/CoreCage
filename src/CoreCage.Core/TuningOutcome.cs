using System.Collections.Generic;
using System.Linq;

namespace CoreCage.Core
{
    /// <summary>How loudly a tuning result should be surfaced.</summary>
    public enum TuningSeverity { Info, Warning, Error }

    /// <summary>A single thing the user should know about a tuning apply (e.g. "CO did NOT apply").</summary>
    public sealed class TuningWarning
    {
        public string Component { get; }
        public string Message { get; }
        public TuningSeverity Severity { get; }

        public TuningWarning(string component, string message, TuningSeverity severity = TuningSeverity.Warning)
        {
            Component = component;
            Message = message;
            Severity = severity;
        }

        public override string ToString() => $"{Component}: {Message}";
    }

    /// <summary>
    /// Collected, surfaceable outcome of a tuning apply (Council 2026-06-01 rank 4). The apply path
    /// used to swallow silent no-ops into the log — "Gaming Mode Active" showed regardless of whether
    /// the SMU actually accepted the Curve-Optimizer write or RyzenAdj applied the power limits. This
    /// type carries those failures back to the caller so the UI can show a toast/InfoBar ("CO did NOT
    /// apply") instead of lying. Pure + unit-tested; the UI consumes <see cref="Summary"/>.
    /// </summary>
    public sealed class TuningOutcome
    {
        private readonly List<TuningWarning> _warnings = new();

        /// <summary>All warnings/errors recorded during the apply, in the order they occurred.</summary>
        public IReadOnlyList<TuningWarning> Warnings => _warnings;

        /// <summary>True when at least one warning or error was recorded.</summary>
        public bool HasWarnings => _warnings.Count > 0;

        /// <summary>True when at least one <see cref="TuningSeverity.Error"/> was recorded.</summary>
        public bool HasErrors => _warnings.Any(w => w.Severity == TuningSeverity.Error);

        public void Add(TuningWarning warning)
        {
            if (warning != null) _warnings.Add(warning);
        }

        public void Add(string component, string message, TuningSeverity severity = TuningSeverity.Warning)
            => Add(new TuningWarning(component, message, severity));

        /// <summary>
        /// A one-line summary suitable for a toast/InfoBar, or <c>null</c> when there is nothing to
        /// surface (a clean apply). Errors are listed before warnings.
        /// </summary>
        public string? Summary()
        {
            if (_warnings.Count == 0) return null;
            var ordered = _warnings
                .OrderByDescending(w => w.Severity)   // Error > Warning > Info
                .Select(w => w.Message);
            return string.Join(" · ", ordered);
        }
    }
}
