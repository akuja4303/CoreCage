using System;

namespace CoreCage.Core
{
    /// <summary>Fallback when the real GPU controller can't construct (no NVIDIA GPU, broken
    /// driver, missing native NVAPI shim) — every operation is a safe no-op so the app still
    /// runs fully, with GPU tuning simply unavailable instead of crashing before the window.</summary>
    public sealed class NullGpuController : IGpuController
    {
        public (int min, int current, int max) GetPowerLimits() => (0, 0, 0);
        public void SetPowerLimit(int watts) { }
        public (int coreMhz, int memMhz, float powerW, float tempC) GetStats() => (0, 0, 0f, 0f);
        public bool SupportsClockOffset => false;
        public int GetCoreClockOffsetMhz() => 0;
        public bool SetCoreClockOffsetMhz(int mhz) => false;
    }
}
