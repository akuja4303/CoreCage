using System;

namespace CoreCage.Core.Telemetry
{
    /// <summary>
    /// Fixed-capacity rolling buffer of <see cref="double"/> samples backed by a single
    /// pre-allocated array. When full, <see cref="Add"/> overwrites the oldest sample.
    /// Designed as the data foundation a live ScottPlot series will bind to: allocation-light
    /// on the hot path (only <see cref="Snapshot"/> allocates), and thread-safe enough for one
    /// writer (the SystemMonitor timer tick) and one reader (the chart) via a simple lock.
    /// </summary>
    public sealed class MetricRingBuffer
    {
        private readonly object _gate = new();
        private readonly double[] _buffer;
        private int _head;   // index where the NEXT sample will be written
        private int _count;  // number of valid samples currently stored

        /// <summary>
        /// Creates a buffer that retains the most recent <paramref name="capacity"/> samples.
        /// </summary>
        /// <param name="capacity">Maximum number of samples to retain; must be &gt;= 1.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> &lt; 1.</exception>
        public MetricRingBuffer(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be at least 1.");

            _buffer = new double[capacity];
        }

        /// <summary>Maximum number of samples the buffer can hold.</summary>
        public int Capacity => _buffer.Length;

        /// <summary>Number of samples currently stored (0..<see cref="Capacity"/>).</summary>
        public int Count
        {
            get { lock (_gate) { return _count; } }
        }

        /// <summary>
        /// The most recently added sample, or <see cref="double.NaN"/> when the buffer is empty.
        /// NaN (rather than 0) is used so an empty/uninitialized series is distinguishable from a
        /// genuine 0 reading by chart/consumer code (NaN is also a natural "no data" gap for plots).
        /// </summary>
        public double Latest
        {
            get
            {
                lock (_gate)
                {
                    if (_count == 0) return double.NaN;
                    // _head points at the next write slot; the newest sample is one slot behind it.
                    int idx = (_head - 1 + _buffer.Length) % _buffer.Length;
                    return _buffer[idx];
                }
            }
        }

        /// <summary>
        /// Appends a sample. When the buffer is full the oldest sample is overwritten so the buffer
        /// always reflects the most recent <see cref="Capacity"/> values.
        /// </summary>
        public void Add(double value)
        {
            lock (_gate)
            {
                _buffer[_head] = value;
                _head = (_head + 1) % _buffer.Length;
                if (_count < _buffer.Length)
                    _count++;
                // else: full — _head has advanced, effectively dropping the oldest sample.
            }
        }

        /// <summary>
        /// Returns the current contents in chronological (oldest → newest) order.
        /// The returned array length equals <see cref="Count"/> (empty array when no samples).
        /// </summary>
        public double[] Snapshot()
        {
            lock (_gate)
            {
                var result = new double[_count];
                if (_count == 0) return result;

                // Oldest sample lives at (_head - _count) modulo capacity.
                int start = (_head - _count + _buffer.Length) % _buffer.Length;
                for (int i = 0; i < _count; i++)
                    result[i] = _buffer[(start + i) % _buffer.Length];

                return result;
            }
        }

        /// <summary>Arithmetic mean of the current contents; 0 when empty.</summary>
        public double Average()
        {
            lock (_gate)
            {
                if (_count == 0) return 0d;
                double sum = 0d;
                for (int i = 0; i < _count; i++)
                    sum += _buffer[i];
                return sum / _count;
            }
        }

        /// <summary>Maximum of the current contents; 0 when empty.</summary>
        public double Max()
        {
            lock (_gate)
            {
                if (_count == 0) return 0d;
                double max = double.NegativeInfinity;
                for (int i = 0; i < _count; i++)
                {
                    double v = _buffer[i];
                    if (v > max) max = v;
                }
                return max;
            }
        }

        /// <summary>Minimum of the current contents; 0 when empty.</summary>
        public double Min()
        {
            lock (_gate)
            {
                if (_count == 0) return 0d;
                double min = double.PositiveInfinity;
                for (int i = 0; i < _count; i++)
                {
                    double v = _buffer[i];
                    if (v < min) min = v;
                }
                return min;
            }
        }
    }
}
