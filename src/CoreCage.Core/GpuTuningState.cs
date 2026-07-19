using System;

namespace CoreCage.Core
{
    /// <summary>
    /// Pure GPU tuning helpers: clamping for clock offsets + power limit, and the kHz unit conversion
    /// NVAPI pstate deltas use. No NVAPI/I-O here so it is fully unit-testable (mirrors
    /// <see cref="TuningState"/> / <see cref="SmuCliState"/>). Ranges reflect this RTX 3060 12GB:
    /// core +150 MHz validated stable (+180 staged); memory OC is dangerous (a +1000 mem OC caused a
    /// DXGI_DEVICE_HUNG) so the memory band is intentionally conservative.
    /// </summary>
    public static class GpuTuningState
    {
        public const int CoreOffsetMin = -500;
        public const int CoreOffsetMax = 1000;
        public const int MemOffsetMin  = -1000;
        public const int MemOffsetMax  = 1000;

        /// <summary>Validated-stable gaming core offset for this card (memory: +150 stable).</summary>
        public const int ValidatedGamingCoreOffsetMhz = 150;

        public static int ClampCoreOffset(int mhz) => Math.Clamp(mhz, CoreOffsetMin, CoreOffsetMax);
        public static int ClampMemOffset(int mhz)  => Math.Clamp(mhz, MemOffsetMin, MemOffsetMax);

        /// <summary>NVAPI pstate clock deltas are expressed in kHz.</summary>
        public static int MhzToKhz(int mhz) => mhz * 1000;

        /// <summary>Clamps a requested power-limit (watts) into the card's reported [min,max].</summary>
        public static int ClampPowerLimit(int watts, int min, int max)
        {
            if (max < min) (min, max) = (max, min);
            return Math.Clamp(watts, min, max);
        }
    }
}
