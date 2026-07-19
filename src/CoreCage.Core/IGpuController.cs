namespace CoreCage.Core
{
    /// <summary>GPU control seam. v1 impl = nvidia-smi power limit only.
    /// v2 impl (NvApiGpuController) adds clock/voltage offsets via NVAPI — same interface.</summary>
    public interface IGpuController
    {
        /// <summary>(min, current, max) power limit in watts.</summary>
        (int min, int current, int max) GetPowerLimits();

        /// <summary>Sets the GPU power limit in watts (clamped to the reported range by the caller).</summary>
        void SetPowerLimit(int watts);

        /// <summary>(coreMhz, memMhz, powerW, tempC) live telemetry.</summary>
        (int coreMhz, int memMhz, float powerW, float tempC) GetStats();

        /// <summary>True if this controller can set a GPU core-clock offset (NVAPI can; nvidia-smi cannot).</summary>
        bool SupportsClockOffset { get; }

        /// <summary>Last core-clock offset applied through this controller, in MHz (0 if none/unsupported).</summary>
        int GetCoreClockOffsetMhz();

        /// <summary>Sets the GPU core-clock offset in MHz (clamped). Returns false if unsupported or it failed.</summary>
        bool SetCoreClockOffsetMhz(int mhz);
    }
}
