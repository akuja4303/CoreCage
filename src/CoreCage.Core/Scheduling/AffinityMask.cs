using System.Collections.Generic;

namespace CoreCage.Core.Scheduling
{
    /// <summary>
    /// Pure CPU-affinity bitmask helpers (no I/O — unit-testable). Bit <c>i</c> set = logical CPU <c>i</c>
    /// is allowed. Used to pin a game to physical cores (skip SMT siblings to avoid intra-core
    /// contention) or to keep it off the OS housekeeping cores. On this Ryzen 5 5600G that's 6 physical
    /// cores / 12 logical threads, single CCX (Windows enumerates SMT siblings as adjacent logicals).
    /// </summary>
    public static class AffinityMask
    {
        /// <summary>Builds a mask from explicit logical-core indices (ignores out-of-range &lt;0 or ≥64).</summary>
        public static long FromCores(IEnumerable<int> cores)
        {
            long m = 0;
            if (cores == null) return 0;
            foreach (int c in cores) if (c >= 0 && c < 64) m |= 1L << c;
            return m;
        }

        /// <summary>Expands a mask back into the set logical-core indices, ascending.</summary>
        public static IReadOnlyList<int> ToCores(long mask)
        {
            var list = new List<int>();
            for (int i = 0; i < 64; i++) if ((mask & (1L << i)) != 0) list.Add(i);
            return list;
        }

        /// <summary>Mask with every logical CPU [0..logicalCount) allowed.</summary>
        public static long AllCores(int logicalCount)
        {
            if (logicalCount <= 0) return 0;
            if (logicalCount >= 64) return -1L; // all 64 bits
            return (1L << logicalCount) - 1;
        }

        /// <summary>
        /// One logical per physical core (the first sibling of each), so a pinned game gets full
        /// physical cores without SMT contention. e.g. 12 logical / 2 threads-per-core → {0,2,4,6,8,10}.
        /// </summary>
        public static long PhysicalCoresOnly(int logicalCount, int threadsPerCore = 2)
        {
            if (logicalCount <= 0 || threadsPerCore <= 0) return 0;
            long m = 0;
            for (int i = 0; i < logicalCount; i += threadsPerCore) m |= 1L << i;
            return m;
        }

        /// <summary>Clears the given cores from a mask (e.g. reserve core 0/1 for the OS).</summary>
        public static long ExcludeCores(long mask, IEnumerable<int> exclude)
        {
            if (exclude == null) return mask;
            foreach (int c in exclude) if (c >= 0 && c < 64) mask &= ~(1L << c);
            return mask;
        }
    }
}
