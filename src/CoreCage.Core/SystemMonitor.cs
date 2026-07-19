using System;
using System.Diagnostics;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace CoreCage.Core
{
    /// <summary>
    /// Real-time system monitoring backed by LibreHardwareMonitor for accurate
    /// CPU/GPU temps, clocks, power, and voltage on all modern hardware.
    /// Windows Performance Counters are kept for CPU load and RAM (faster, no driver needed).
    /// </summary>
    public static class SystemMonitor
    {
        // ── Performance counters (lightweight, no driver) ────────────────────
        private static PerformanceCounter? _cpuCounter;
        private static PerformanceCounter? _availableRamCounter;
        private static bool _isInitialized;

        // ── LibreHardwareMonitor ─────────────────────────────────────────────
        private static Computer? _computer;
        private static bool _lhmAvailable;
        private static DateTime _lhmLastUpdate = DateTime.MinValue;

        // ── Cached LHM sensor values ─────────────────────────────────────────
        private static float? _cachedCpuTemp;
        private static float? _cachedGpuTemp;
        private static float  _cachedGpuUsage;
        private static float  _cachedCpuClock;   // MHz — max boost clock
        private static float  _cachedCpuPower;   // Watts — package power
        private static float  _cachedCpuVoltage; // Volts — core voltage

        // ── Cached slow-changing values ──────────────────────────────────────
        private static string? _cachedCpuName;
        private static string? _cachedGpuName;
        private static string? _cachedOsName;
        private static long    _cachedTotalRam = -1;

        // ── Power plan (polled every 10 s via background task) ───────────────
        private static string  _cachedPowerPlan = "Unknown";
        private static DateTime _powerPlanLastFetch = DateTime.MinValue;

        /// <summary>
        /// Opens performance counters and LibreHardwareMonitor. Call once at startup.
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
                _availableRamCounter = new PerformanceCounter("Memory", "Available Bytes");
                _availableRamCounter.NextValue();
            }
            catch (Exception ex) { Logger.LogError("Performance counter init failed", ex); }

            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = false,
                    IsMotherboardEnabled = true,
                    IsStorageEnabled = false,
                    IsNetworkEnabled = false,
                };
                _computer.Open();
                _lhmAvailable = true;
                Logger.Log("LibreHardwareMonitor initialized — real temps/clocks/power active");
            }
            catch (Exception ex)
            {
                Logger.LogError("LibreHardwareMonitor init failed (app continues with WMI fallback)", ex);
                _lhmAvailable = false;
            }

            _isInitialized = true;
        }

        /// <summary>Closes the LibreHardwareMonitor computer object. Call on app exit.</summary>
        public static void Shutdown()
        {
            try { _computer?.Close(); } catch { }
            try { _cpuCounter?.Dispose(); } catch { }
            try { _availableRamCounter?.Dispose(); } catch { }
        }

        // ── Refresh LHM (throttled to once per second) ───────────────────────
        private static void RefreshLhm()
        {
            if (!_lhmAvailable || _computer == null) return;
            if ((DateTime.Now - _lhmLastUpdate).TotalSeconds < 1) return;
            _lhmLastUpdate = DateTime.Now;

            try
            {
                float maxClock = 0;
                bool gotNvidiaGpuLoad = false;   // prefer the discrete NVIDIA GPU's load over an iGPU
                bool gotNvidiaGpuTemp = false;   // ...and its CORE temp over hot-spot/memory/iGPU

                foreach (var hw in _computer.Hardware)
                {
                    hw.Update();
                    foreach (var sub in hw.SubHardware) sub.Update();

                    if (hw.HardwareType == HardwareType.Cpu)
                    {
                        foreach (var s in hw.Sensors)
                        {
                            if (!s.Value.HasValue) continue;
                            float v = s.Value.Value;

                            switch (s.SensorType)
                            {
                                case SensorType.Temperature:
                                    // Prefer "CPU Package", "Tdie", or any CPU temp. Some chips
                                    // (e.g. Ryzen APUs) expose the sensor but read 0 without a
                                    // working driver — ≤1°C is "no reading", never a real CPU
                                    // temp; keep null so the UI shows unknown instead of a green 0°.
                                    if ((s.Name.Contains("Package") || s.Name.Contains("Tdie") ||
                                        s.Name.StartsWith("CPU")) && v > 1f)
                                        _cachedCpuTemp = v;
                                    break;

                                case SensorType.Clock:
                                    // Track highest core clock (= current boost)
                                    if (s.Name.Contains("Core") && v > maxClock)
                                        maxClock = v;
                                    break;

                                case SensorType.Power:
                                    if (s.Name.Contains("Package"))
                                        _cachedCpuPower = v;
                                    break;

                                case SensorType.Voltage:
                                    if (s.Name.Contains("Core") || s.Name.Contains("VCore"))
                                        _cachedCpuVoltage = v;
                                    break;
                            }
                        }
                        if (maxClock > 0) _cachedCpuClock = maxClock;
                    }
                    else if (hw.HardwareType == HardwareType.GpuNvidia ||
                             hw.HardwareType == HardwareType.GpuAmd    ||
                             hw.HardwareType == HardwareType.GpuIntel)
                    {
                        foreach (var s in hw.Sensors)
                        {
                            if (!s.Value.HasValue) continue;
                            float v = s.Value.Value;

                            switch (s.SensorType)
                            {
                                case SensorType.Temperature:
                                    // Prefer the discrete NVIDIA GPU's CORE temp. The old broad
                                    // Contains("GPU")||Contains("Core")||"Temperature" match also
                                    // grabbed "GPU Hot Spot" / "GPU Memory Junction" (both run
                                    // ~10-15°C hotter) and the iGPU — whichever iterated last won,
                                    // reporting ~53°C while the 3060 core was 41°C. Take the exact
                                    // "GPU Core" sensor, NVIDIA over iGPU. (≤1°C = no reading.)
                                    if (s.Name == "GPU Core" && v > 1f)
                                    {
                                        if (hw.HardwareType == HardwareType.GpuNvidia)
                                        { _cachedGpuTemp = v; gotNvidiaGpuTemp = true; }
                                        else if (!gotNvidiaGpuTemp)
                                        { _cachedGpuTemp = v; }
                                    }
                                    break;

                                case SensorType.Load:
                                    // ONLY the discrete GPU's CORE load. The old
                                    // Contains("GPU")||Contains("Core") match also grabbed
                                    // "GPU Memory Controller" / "GPU Video Engine" / "GPU Power"
                                    // AND the 5600G's integrated Radeon — whichever iterated last
                                    // won, sticking the reading at ~94% while the real 3060 sat at 2%.
                                    // Take the exact "GPU Core" sensor, and let the NVIDIA dGPU win
                                    // over the iGPU regardless of iteration order.
                                    if (s.Name == "GPU Core")
                                    {
                                        if (hw.HardwareType == HardwareType.GpuNvidia)
                                        { _cachedGpuUsage = v; gotNvidiaGpuLoad = true; }
                                        else if (!gotNvidiaGpuLoad)
                                        { _cachedGpuUsage = v; }
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.LogError("LHM refresh error", ex); }
        }

        // ── CPU usage (Performance Counter — fast) ───────────────────────────
        public static float GetCpuUsage()
        {
            try
            {
                if (_cpuCounter == null) Initialize();
                return _cpuCounter?.NextValue() ?? 0f;
            }
            catch { return 0f; }
        }

        // ── RAM (Performance Counter + WMI for total) ────────────────────────
        public static long GetAvailableRAM()
        {
            try
            {
                if (_availableRamCounter == null) Initialize();
                return (long)(_availableRamCounter?.NextValue() ?? 0f);
            }
            catch { return 0; }
        }

        public static long GetTotalRAM()
        {
            if (_cachedTotalRam >= 0) return _cachedTotalRam;
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                {
                    _cachedTotalRam = Convert.ToInt64(obj["TotalPhysicalMemory"]);
                    return _cachedTotalRam;
                }
            }
            catch { }
            _cachedTotalRam = 0;
            return 0;
        }

        public static double GetRAMUsagePercent()
        {
            long total     = GetTotalRAM();
            long available = GetAvailableRAM();
            return total > 0 ? (double)(total - available) / total * 100.0 : 0;
        }

        // ── Hardware names (LHM first, WMI fallback) ─────────────────────────
        public static string GetCpuName()
        {
            if (_cachedCpuName != null) return _cachedCpuName;

            if (_lhmAvailable && _computer != null)
            {
                foreach (var hw in _computer.Hardware)
                {
                    if (hw.HardwareType == HardwareType.Cpu && !string.IsNullOrEmpty(hw.Name))
                    {
                        _cachedCpuName = hw.Name;
                        return _cachedCpuName;
                    }
                }
            }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    _cachedCpuName = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                    return _cachedCpuName;
                }
            }
            catch { }
            _cachedCpuName = "Unknown";
            return _cachedCpuName;
        }

        public static string GetGpuName()
        {
            if (_cachedGpuName != null) return _cachedGpuName;

            if (_lhmAvailable && _computer != null)
            {
                foreach (var hw in _computer.Hardware)
                {
                    if ((hw.HardwareType == HardwareType.GpuNvidia ||
                         hw.HardwareType == HardwareType.GpuAmd    ||
                         hw.HardwareType == HardwareType.GpuIntel) &&
                        !string.IsNullOrEmpty(hw.Name))
                    {
                        _cachedGpuName = hw.Name;
                        return _cachedGpuName;
                    }
                }
            }

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    _cachedGpuName = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                    return _cachedGpuName;
                }
            }
            catch { }
            _cachedGpuName = "Unknown";
            return _cachedGpuName;
        }

        public static string GetOSName()
        {
            if (_cachedOsName != null) return _cachedOsName;
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Caption FROM Win32_OperatingSystem");
                foreach (var obj in searcher.Get())
                {
                    _cachedOsName = obj["Caption"]?.ToString()?.Trim()
                                    ?? Environment.OSVersion.ToString();
                    return _cachedOsName;
                }
            }
            catch { }
            _cachedOsName = Environment.OSVersion.ToString();
            return _cachedOsName;
        }

        // ── LHM sensor accessors ─────────────────────────────────────────────
        /// <summary>CPU package temperature in °C. Null if unavailable.</summary>
        public static float? GetCpuTemperature()
        {
            RefreshLhm();
            return _cachedCpuTemp;
        }

        /// <summary>GPU core temperature in °C. Null if unavailable.</summary>
        public static float? GetGpuTemperature()
        {
            RefreshLhm();
            return _cachedGpuTemp;
        }

        /// <summary>GPU core load %. Returns 0 if unavailable.</summary>
        public static float GetGpuUsage()
        {
            RefreshLhm();
            return _cachedGpuUsage;
        }

        /// <summary>Highest CPU core clock in MHz (current boost). Returns 0 if unavailable.</summary>
        public static float GetCpuClockSpeed()
        {
            RefreshLhm();
            return _cachedCpuClock;
        }

        /// <summary>CPU package power draw in Watts. Returns 0 if unavailable.</summary>
        public static float GetCpuPower()
        {
            RefreshLhm();
            return _cachedCpuPower;
        }

        /// <summary>CPU core voltage in Volts. Returns 0 if unavailable.</summary>
        public static float GetCpuVoltage()
        {
            RefreshLhm();
            return _cachedCpuVoltage;
        }

        // ── Power plan (background refresh, never blocks UI) ─────────────────
        public static string GetPowerPlan()
        {
            if ((DateTime.Now - _powerPlanLastFetch).TotalSeconds < 10)
                return _cachedPowerPlan;

            _powerPlanLastFetch = DateTime.Now;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powercfg",
                        Arguments = "/getactivescheme",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    };
                    using var proc = Process.Start(psi);
                    string output = proc?.StandardOutput.ReadToEnd() ?? "";
                    proc?.WaitForExit(3000);

                    int start = output.IndexOf('(') + 1;
                    int end   = output.IndexOf(')');
                    if (start > 0 && end > start)
                        _cachedPowerPlan = output.Substring(start, end - start).Trim();
                }
                catch { }
            });

            return _cachedPowerPlan;
        }

        /// <summary>Per-core snapshot from the existing LHM instance (read-only). Empty snapshot if LHM unavailable.</summary>
        public static CoreCage.Core.Monitor.CpuSnapshot GetCpuSnapshot()
        {
            if (!_lhmAvailable || _computer == null)
                return new CoreCage.Core.Monitor.CpuSnapshot { Name = GetCpuName() };

            var readings = new System.Collections.Generic.List<CoreCage.Core.Monitor.SensorReading>();
            string name = GetCpuName();
            try
            {
                foreach (var hw in _computer.Hardware)
                {
                    if (hw.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.Cpu) continue;
                    hw.Update();
                    name = string.IsNullOrEmpty(hw.Name) ? name : hw.Name;
                    foreach (var s in hw.Sensors)
                        if (s.Value.HasValue)
                            readings.Add(new CoreCage.Core.Monitor.SensorReading(s.Name, s.SensorType.ToString(), s.Value.Value));
                }
            }
            catch (System.Exception ex) { Logger.LogError("GetCpuSnapshot LHM read failed", ex); }

            return CoreCage.Core.Monitor.CpuSnapshot.BuildSnapshot(readings, name);
        }

        // ── Utility ──────────────────────────────────────────────────────────
        public static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double size = bytes;
            while (size >= 1024 && i < suffixes.Length - 1) { size /= 1024; i++; }
            return $"{size:F2} {suffixes[i]}";
        }
    }
}
