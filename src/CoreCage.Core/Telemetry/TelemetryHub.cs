using System;
using System.Collections.Generic;

namespace CoreCage.Core.Telemetry
{
    /// <summary>
    /// Central registry of named <see cref="MetricRingBuffer"/> series for the live dashboard.
    /// The SystemMonitor timer tick pushes samples in (<see cref="Push"/>) and a ScottPlot
    /// DataStreamer reads them back (<see cref="Get"/> / <see cref="Series"/>). This type is the
    /// data foundation only — it is intentionally not wired to any monitor or UI here.
    /// </summary>
    public sealed class TelemetryHub
    {
        /// <summary>Default per-series capacity: 120 samples ≈ 4 minutes at a 2s monitor cadence.</summary>
        public const int DefaultCapacity = 120;

        // Well-known series keys the dashboard cares about. Push() will lazily create any of these
        // (or any custom key) on first use, so callers don't need to pre-register.
        public const string Cpu = "cpu";
        public const string Gpu = "gpu";
        public const string Ram = "ram";
        public const string FrameTime = "frametime";

        private static readonly Lazy<TelemetryHub> _instance = new(() => new TelemetryHub());

        /// <summary>Process-wide singleton instance.</summary>
        public static TelemetryHub Instance => _instance.Value;

        private readonly object _gate = new();
        private readonly Dictionary<string, MetricRingBuffer> _series = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Public constructor is allowed so the type stays fully unit-testable in isolation;
        /// production code should normally use <see cref="Instance"/>.
        /// </summary>
        public TelemetryHub()
        {
        }

        /// <summary>
        /// Read-only view of all registered series keyed by name. Iterating this snapshot is safe
        /// even while the writer keeps appending samples to the underlying buffers.
        /// </summary>
        public IReadOnlyDictionary<string, MetricRingBuffer> Series
        {
            get
            {
                lock (_gate)
                {
                    // Return a copy so external enumeration can't observe a mid-mutation dictionary.
                    return new Dictionary<string, MetricRingBuffer>(_series, StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        /// <summary>
        /// Appends <paramref name="value"/> to the named series, creating the series (with
        /// <see cref="DefaultCapacity"/>) on first use.
        /// </summary>
        /// <param name="series">Series key (e.g. "cpu"); must be non-null/non-whitespace.</param>
        /// <param name="value">Sample value to record.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="series"/> is null or whitespace.</exception>
        public void Push(string series, double value)
        {
            if (string.IsNullOrWhiteSpace(series))
                throw new ArgumentException("Series name must be non-empty.", nameof(series));

            MetricRingBuffer buffer = GetOrAdd(series);
            buffer.Add(value);
        }

        /// <summary>
        /// Returns the buffer for <paramref name="series"/>, or <c>null</c> if it has never been pushed to.
        /// </summary>
        public MetricRingBuffer? Get(string series)
        {
            if (string.IsNullOrWhiteSpace(series))
                return null;

            lock (_gate)
            {
                return _series.TryGetValue(series, out var buffer) ? buffer : null;
            }
        }

        private MetricRingBuffer GetOrAdd(string series)
        {
            lock (_gate)
            {
                if (!_series.TryGetValue(series, out var buffer))
                {
                    buffer = new MetricRingBuffer(DefaultCapacity);
                    _series[series] = buffer;
                }
                return buffer;
            }
        }
    }
}
