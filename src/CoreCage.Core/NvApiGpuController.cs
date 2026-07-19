using System;
using NvAPIWrapper;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;

namespace CoreCage.Core
{
    /// <summary>
    /// v2 GPU controller (docs/UPGRADES.md TIER 1.1). Adds native NVAPI core-clock-offset control —
    /// the lever behind the "low power limit + positive core offset = effective undervolt" trick that
    /// nvidia-smi cannot do. Power limit + telemetry still go through the proven nvidia-smi path
    /// (real watts), so this is a strict superset of the legacy nvidia-smi-only path.
    ///
    /// All NVAPI access is guarded: if init fails (driver/version), <see cref="IsAvailable"/> is false
    /// and offset writes no-op. NOT instantiated by the UI yet — wiring a live clock offset into a
    /// preset/auto-tune is a hardware-validated step (GPU instability triggers a driver TDR; milder
    /// than a CPU CO freeze, but still validate before shipping it on).
    /// </summary>
    public class NvApiGpuController : IGpuController
    {
        private readonly PhysicalGPU? _gpu;
        private int _lastCoreOffsetMhz;

        public NvApiGpuController()
        {
            try
            {
                NVIDIA.Initialize();
                PhysicalGPU[] gpus = PhysicalGPU.GetPhysicalGPUs();
                _gpu = gpus.Length > 0 ? gpus[0] : null;
                Logger.Log(_gpu != null ? "NVAPI initialised — clock-offset control available"
                                        : "NVAPI initialised but no physical GPU found");
            }
            catch (Exception ex)
            {
                Logger.LogError("NVAPI init failed; falling back to nvidia-smi (no clock offset)", ex);
                _gpu = null;
            }
        }

        public bool IsAvailable => _gpu != null;
        public bool SupportsClockOffset => _gpu != null;

        // Power limit + stats reuse the nvidia-smi path (reports real watts/MHz).
        public (int min, int current, int max) GetPowerLimits() => PerformanceTuner.GetGpuPowerLimits();
        public void SetPowerLimit(int watts) => PerformanceTuner.SetGpuPowerLimit(watts);
        public (int coreMhz, int memMhz, float powerW, float tempC) GetStats() => PerformanceTuner.GetGpuStats();

        public int GetCoreClockOffsetMhz() => _lastCoreOffsetMhz;

        public bool SetCoreClockOffsetMhz(int mhz)
        {
            if (_gpu == null) { Logger.Log("NVAPI unavailable — core clock offset not set"); return false; }
            int clamped = GpuTuningState.ClampCoreOffset(mhz);
            try
            {
                // Apply the offset as a P0 (3D-performance) graphics-clock delta. Delta is in kHz.
                var delta      = new PerformanceStates20ParameterDelta(GpuTuningState.MhzToKhz(clamped));
                var clockEntry = new PerformanceStates20ClockEntryV1(
                    PublicClockDomain.Graphics, PerformanceStates20ClockType.Single, delta);
                var pstate = new PerformanceStates20InfoV1.PerformanceState20(
                    PerformanceStateId.P0_3DPerformance,
                    new[] { clockEntry },
                    Array.Empty<PerformanceStates20BaseVoltageEntryV1>());
                var info = new PerformanceStates20InfoV1(new[] { pstate }, clocksCount: 1u, baseVoltagesCount: 0u);

                GPUApi.SetPerformanceStates20(_gpu.Handle, info);
                _lastCoreOffsetMhz = clamped;
                Logger.Event("NVAPI core clock offset set to {Offset} MHz", clamped);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"NVAPI core clock offset ({clamped} MHz) failed", ex);
                return false;
            }
        }

        // ---- Digital Vibrance (color "pop") via NVAPI DVC. Range is typically 0-63. ----
        public (bool ok, int current, int min, int max, int def) GetVibrance()
        {
            try
            {
                var displays = NvAPIWrapper.Display.Display.GetDisplays();
                if (displays.Length == 0) return (false, 0, 0, 63, 0);
                var info = NvAPIWrapper.Native.DisplayApi.GetDVCInfoEx(displays[0].Handle);
                return (true, info.CurrentLevel, info.MinimumLevel, info.MaximumLevel, info.DefaultLevel);
            }
            catch (Exception ex)
            {
                Logger.LogError("NVAPI get digital vibrance failed", ex);
                return (false, 0, 0, 63, 0);
            }
        }

        /// <summary>Set digital vibrance on every connected display (0 = default, higher = more saturated).</summary>
        public bool SetVibrance(int level)
        {
            try
            {
                var displays = NvAPIWrapper.Display.Display.GetDisplays();
                if (displays.Length == 0) return false;
                foreach (var d in displays)
                    NvAPIWrapper.Native.DisplayApi.SetDVCLevelEx(d.Handle, level);
                Logger.Event("NVAPI digital vibrance set to {Level}", level);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"NVAPI set digital vibrance ({level}) failed", ex);
                return false;
            }
        }
    }
}
