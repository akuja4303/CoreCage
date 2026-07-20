using System;
using System.Threading.Tasks;
using CoreCage.Core.Telemetry;

namespace CoreCage.Core.Benchmark
{
    /// <summary>
    /// The "Prove it" primitive: bench (baseline) → apply (the tweak) → bench (after), with an
    /// optional revert once the comparison is captured. Every step is an injected delegate so the
    /// sequencing here is unit-testable with fakes; the production caller wires <c>bench</c> to a real
    /// <see cref="PresentMonInterface"/> capture and <c>apply</c> to an <c>IModeModule.ApplyAsync</c>
    /// (or similar) call. This class never touches PresentMon or the OS itself — it only calls what
    /// it's given, in order.
    /// </summary>
    public sealed class AbBenchRunner
    {
        private readonly Func<Task<FrametimeStats>> _bench;
        private readonly Func<Task> _apply;
        private readonly Func<Task>? _revert;

        public AbBenchRunner(Func<Task<FrametimeStats>> bench, Func<Task> apply, Func<Task>? revert = null)
        {
            _bench = bench ?? throw new ArgumentNullException(nameof(bench));
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            _revert = revert;
        }

        /// <summary>Runs bench → apply → bench (→ revert if given) and returns the before/after stats.</summary>
        public async Task<(FrametimeStats Before, FrametimeStats After)> RunAsync()
        {
            FrametimeStats before = await _bench().ConfigureAwait(false);
            await _apply().ConfigureAwait(false);
            FrametimeStats after = await _bench().ConfigureAwait(false);
            if (_revert != null) await _revert().ConfigureAwait(false);
            return (before, after);
        }
    }
}
