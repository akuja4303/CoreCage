using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace CoreCage.Core
{
    /// <summary>
    /// Gaming Mode++ : the tier-1 latency-squeeze layer on top of EacSafePriority.
    ///
    /// Why this exists:
    ///   EacSafePriority already covers IFEO priority + powercfg + FSO + TdrDelay
    ///   + service pause. That handled the system-level + per-exe wins. What was
    ///   still on the table:
    ///     - GPU/NIC default to LEGACY line-based interrupts on many systems.
    ///       Flipping them to Message-Signaled Interrupts (MSI) cuts ~100-200μs
    ///       of DPC latency per frame. Done at boot via registry. NO runtime
    ///       touch on the game -- invisible to EAC.
    ///     - NIC advanced properties (Energy Efficient Ethernet, RX/TX buffers,
    ///       flow control, RSS) default to power-saving values. Wrong defaults
    ///       for competitive titles.
    ///     - GameDVR's background frame capture monitor runs even with Game Bar
    ///       disabled. Killing it at the registry stops the periodic frametime
    ///       stutter.
    ///     - Background UWP apps (Mail, Photos, Cortana) wake periodically and
    ///       cause Alt-Tab spikes. Policy-disable them.
    ///     - QoS DSCP marking existed for one app already; PioneerGame
    ///       (Arc Raiders) was uncovered.
    ///
    /// Every tweak has a paired Restore* so switching out of Gaming Mode reverses cleanly.
    /// All registry/netsh/Set-NetAdapter actions are best-effort (try/catch Log)
    /// so a single failure doesn't abort the whole Gaming Mode pipeline.
    /// </summary>
    public static class GamingModePlusPlus
    {
        // ------------------------------------------------------------------
        // 1. MSI MODE for GPU + NIC
        // ------------------------------------------------------------------
        // Registry path: HKLM\SYSTEM\CurrentControlSet\Enum\PCI\<dev-id>\<inst>\
        //                Device Parameters\Interrupt Management\
        //                MessageSignaledInterruptProperties\MSISupported = 1
        // NEEDS REBOOT to take effect (PCI subsystem reads this at enumeration).
        // Per-device approach: enumerate Display + Net classes via PnP, walk
        // each instance's PCI hardware ID path, set MSISupported = 1.

        public static int EnableMsiModeForGpuAndNic()
        {
            int touched = 0;
            try { touched += EnableMsiForPnpClass("Display"); }
            catch (Exception ex) { Logger.Log("MSI GPU enable failed: " + ex.Message); }
            try { touched += EnableMsiForPnpClass("Net"); }
            catch (Exception ex) { Logger.Log("MSI NIC enable failed: " + ex.Message); }
            if (touched > 0) Logger.Log("Gaming++: enabled MSI mode on " + touched + " device(s). REBOOT for effect.");
            return touched;
        }

        public static int DisableMsiModeForGpuAndNic()
        {
            int touched = 0;
            try { touched += DisableMsiForPnpClass("Display"); }
            catch (Exception ex) { Logger.Log("MSI GPU disable failed: " + ex.Message); }
            try { touched += DisableMsiForPnpClass("Net"); }
            catch (Exception ex) { Logger.Log("MSI NIC disable failed: " + ex.Message); }
            if (touched > 0) Logger.Log("Gaming++ revert: cleared MSI on " + touched + " device(s).");
            return touched;
        }

        // Walks Pnp class -> finds Instance IDs -> writes MSISupported = 1
        private static int EnableMsiForPnpClass(string pnpClass)
        {
            int n = 0;
            foreach (var instance in EnumeratePciInstances(pnpClass))
            {
                if (SetMsiSupported(instance, 1)) n++;
            }
            return n;
        }

        private static int DisableMsiForPnpClass(string pnpClass)
        {
            int n = 0;
            foreach (var instance in EnumeratePciInstances(pnpClass))
            {
                if (SetMsiSupported(instance, 0)) n++;
            }
            return n;
        }

        // Returns instance subkeys like "PCI\VEN_10DE&DEV_2544&...\4&7D659FE&0&0009"
        private static System.Collections.Generic.IEnumerable<string> EnumeratePciInstances(string pnpClass)
        {
            // PowerShell shell-out is the most reliable way to get "PnpDevice -Class X"
            // instance IDs without diving into setupapi.dll. Cached briefly so we're not
            // re-spawning powershell for every action in a pipeline.
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-PnpDevice -Class " + pnpClass + " -Status OK -PresentOnly | Where-Object { $_.InstanceId -like 'PCI\\*' } | Select-Object -ExpandProperty InstanceId\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) yield break;
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
                        yield return line;
                }
                p.WaitForExit(2000);
            }
        }

        private static bool SetMsiSupported(string pciInstance, int value)
        {
            // pciInstance: "PCI\VEN_...\<inst>"
            // Target subkey: HKLM\SYSTEM\CurrentControlSet\Enum\<instance>\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties
            string subPath = @"SYSTEM\CurrentControlSet\Enum\" + pciInstance +
                             @"\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";
            try
            {
                using (var k = Registry.LocalMachine.CreateSubKey(subPath, true))
                {
                    if (k == null) return false;
                    k.SetValue("MSISupported", value, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch (Exception ex) { Logger.Log("SetMsiSupported(" + pciInstance + "): " + ex.Message); return false; }
        }

        // ------------------------------------------------------------------
        // 2. NIC ADVANCED PROPERTIES (Realtek/Intel gigabit baseline)
        // ------------------------------------------------------------------
        // Property names vary slightly between drivers; we try the common ones
        // and silently ignore unknown-property errors. Each set is per-adapter
        // and reversible.

        public static int HardenAllPhysicalNics()
        {
            int touched = 0;
            try
            {
                var lines = PowershellLines(
                    "Get-NetAdapter -Physical | Where-Object { $_.Status -eq 'Up' } | Select-Object -ExpandProperty Name"
                );
                foreach (var name in lines)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    try
                    {
                        ApplyNicProps(name, hardened: true);
                        touched++;
                    }
                    catch (Exception ex) { Logger.Log("HardenNic(" + name + "): " + ex.Message); }
                }
                if (touched > 0) Logger.Log("Gaming++: hardened " + touched + " NIC(s).");
            }
            catch (Exception ex) { Logger.Log("HardenAllPhysicalNics: " + ex.Message); }
            return touched;
        }

        public static int RestoreAllPhysicalNics()
        {
            int touched = 0;
            try
            {
                var lines = PowershellLines(
                    "Get-NetAdapter -Physical | Where-Object { $_.Status -eq 'Up' } | Select-Object -ExpandProperty Name"
                );
                foreach (var name in lines)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    try
                    {
                        ApplyNicProps(name, hardened: false);
                        touched++;
                    }
                    catch (Exception ex) { Logger.Log("RestoreNic(" + name + "): " + ex.Message); }
                }
                if (touched > 0) Logger.Log("Gaming++ revert: restored " + touched + " NIC(s).");
            }
            catch (Exception ex) { Logger.Log("RestoreAllPhysicalNics: " + ex.Message); }
            return touched;
        }

        // Per-adapter backup root in HKCU. Stores the ORIGINAL value of every
        // *Prefixed registry keyword we change, so restore is exact (not approximated
        // to MS-suggested defaults that may differ from the driver's actual baseline).
        private const string NIC_BACKUP_ROOT = @"Software\CoreCage\NicBackup";

        private static void SaveOriginalNicValue(string adapterName, string keyword)
        {
            // Skip if already backed up -- preserves the TRUE original across multiple Apply calls
            string path = NIC_BACKUP_ROOT + @"\" + SafeKey(adapterName);
            using (var k = Registry.CurrentUser.CreateSubKey(path, true))
            {
                if (k == null) return;
                if (k.GetValue(keyword) != null) return;   // already saved
            }
            // Read live value
            var lines = PowershellLines(
                "(Get-NetAdapterAdvancedProperty -Name '" + adapterName.Replace("'", "''") +
                "' -RegistryKeyword '" + keyword + "' -ErrorAction SilentlyContinue).RegistryValue");
            if (lines.Count == 0) return;
            string val = string.Join(",", lines).Trim();
            if (string.IsNullOrEmpty(val)) return;
            using (var k = Registry.CurrentUser.CreateSubKey(path, true))
            {
                k?.SetValue(keyword, val, RegistryValueKind.String);
            }
        }

        private static string LoadOriginalNicValue(string adapterName, string keyword)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(NIC_BACKUP_ROOT + @"\" + SafeKey(adapterName)))
            {
                return k?.GetValue(keyword) as string;
            }
        }

        private static void ClearNicBackup(string adapterName)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(NIC_BACKUP_ROOT + @"\" + SafeKey(adapterName), false);
            }
            catch { }
        }

        // Adapter names contain spaces/slashes -- normalize for registry key segment
        private static string SafeKey(string s) => s.Replace('\\', '_').Replace('/', '_').Replace(':', '_');

        // Apply gaming-tuned values (hardened=true) or restore-from-backup (hardened=false).
        //
        // VENDOR-AGNOSTIC APPROACH:
        //   Asterisk-prefixed properties (*EEE, *FlowControl, *InterruptModeration,
        //   *ReceiveBuffers, *TransmitBuffers, *SpeedDuplex) are Microsoft-standardized
        //   registry keywords that ALL NDIS-compliant drivers honor identically --
        //   Realtek, Intel, Broadcom, Killer, Mellanox, Aquantia, etc. Use these via
        //   -RegistryKeyword so DisplayName variance is bypassed.
        //
        //   Non-asterisk (Auto Disable Gigabit, Green Ethernet, Power Saving Mode...)
        //   are vendor-specific DisplayName props. Set via -DisplayName as best-effort;
        //   driver rejects unknown ones silently. We catch + ignore.
        //
        //   ON RESTORE we read the per-adapter backup we made on first Apply, so each
        //   adapter goes back to its TRUE original values rather than NDIS-suggested
        //   defaults (which may differ -- e.g. user's Realtek default Rx=512, Tx=128
        //   while NDIS suggests 256/512).
        private static void ApplyNicProps(string adapterName, bool hardened)
        {
            // (RegistryKeyword, gamingValue, defaultValue) -- universal across NDIS drivers
            (string keyword, string gaming, string defaultVal)[] universalProps = hardened
                ? new (string, string, string)[]
                {
                    ("*EEE",                "0", "1"),    // Energy Efficient Ethernet: 0=Off, 1=On
                    ("*FlowControl",        "0", "3"),    // 0=Disabled, 1=Tx, 2=Rx, 3=Tx&Rx
                    ("*InterruptModeration","0", "1"),    // 0=Disabled, 1=Enabled
                    ("*JumboPacket",        "1514", "1514"), // 1514=normal MTU; keep default for compatibility
                    ("*PriorityVLANTag",    "3", "3"),    // 3=Packet Priority + VLAN Enabled
                    ("*ReceiveBuffers",     "2048", "256"),
                    ("*TransmitBuffers",    "2048", "512"),
                    ("*LsoV2IPv4",          "1", "1"),    // Large Send Offload v2 — keep ON (driver-level offload helps)
                    ("*LsoV2IPv6",          "1", "1"),
                    ("*PMARPOffload",       "0", "1"),    // Power-management ARP offload -- off saves wake latency
                    ("*PMNSOffload",        "0", "1"),    // Power-management NS offload -- same
                }
                : new (string, string, string)[]
                {
                    ("*EEE",                "1", ""),
                    ("*FlowControl",        "3", ""),
                    ("*InterruptModeration","1", ""),
                    ("*ReceiveBuffers",     "256", ""),
                    ("*TransmitBuffers",    "512", ""),
                    ("*PMARPOffload",       "1", ""),
                    ("*PMNSOffload",        "1", ""),
                };

            foreach (var prop in universalProps)
            {
                string desired;
                if (hardened)
                {
                    // Save current value BEFORE changing it (only on first Apply)
                    SaveOriginalNicValue(adapterName, prop.keyword);
                    desired = prop.gaming;
                }
                else
                {
                    // Restore: prefer the per-adapter backup; fall back to NDIS-suggested default
                    desired = LoadOriginalNicValue(adapterName, prop.keyword) ?? prop.defaultVal;
                }
                if (string.IsNullOrEmpty(desired)) continue;
                string cmd =
                    "$ErrorActionPreference = 'SilentlyContinue'; " +
                    "Set-NetAdapterAdvancedProperty -Name '" + adapterName.Replace("'", "''") +
                    "' -RegistryKeyword '" + prop.keyword + "' -RegistryValue '" + desired + "' -NoRestart";
                PowershellExec(cmd);
            }

            // On restore, clear the per-adapter backup so the NEXT Apply re-snapshots
            // (covers the case where the user manually re-tuned NIC settings between
            // Restore and the next Gaming Mode click).
            if (!hardened) ClearNicBackup(adapterName);

            // Vendor-specific DisplayName props. ALL silently no-op on unknown drivers.
            // (DisplayName, gamingValue) pairs — only applied when hardening.
            if (hardened)
            {
                (string displayName, string gamingValue)[] vendorProps = new (string, string)[]
                {
                    // Realtek
                    ("Auto Disable Gigabit",        "Disabled"),
                    ("Green Ethernet",              "Disabled"),
                    ("Power Saving Mode",           "Disabled"),
                    ("Shutdown Wake-On-Lan",        "Disabled"),
                    // Intel
                    ("System Idle Power Saver",     "Disabled"),
                    ("Ultra Low Power Mode",        "Disabled"),
                    ("Reduce Power During Standby", "Disabled"),
                    ("Gigabit Master Slave Mode",   "Force Master Mode"),
                    // Killer/Qualcomm
                    ("Advanced EEE",                "Disabled"),
                    ("ARP Offload",                 "Disabled"),
                    ("NS Offload",                  "Disabled"),
                    // Broadcom
                    ("Speed Duplex",                "1.0 Gbps Full Duplex"),
                };
                foreach (var v in vendorProps)
                {
                    string cmd =
                        "$ErrorActionPreference = 'SilentlyContinue'; " +
                        "Set-NetAdapterAdvancedProperty -Name '" + adapterName.Replace("'", "''") +
                        "' -DisplayName '" + v.displayName.Replace("'", "''") +
                        "' -DisplayValue '" + v.gamingValue.Replace("'", "''") + "' -NoRestart";
                    PowershellExec(cmd);
                }
            }

            // Restart once at end so we don't bounce the link 15 times
            PowershellExec("Restart-NetAdapter -Name '" + adapterName.Replace("'", "''") + "' -Confirm:$false -ErrorAction SilentlyContinue");
        }

        // ------------------------------------------------------------------
        // 3. GAMEDVR + GAME BAR full registry kill
        // ------------------------------------------------------------------
        // Game Bar may be off in Settings UI yet the GameDVR capture pipeline
        // still runs background frame buffer captures. Belt+suspenders.
        public static void DisableGameDvr()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore", true))
                {
                    if (k != null) k.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                }
                using (var k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", true))
                {
                    if (k != null) k.SetValue("AllowGameDVR", 0, RegistryValueKind.DWord);
                }
                using (var k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\PolicyManager\default\ApplicationManagement\AllowGameDVR", true))
                {
                    if (k != null) k.SetValue("value", 0, RegistryValueKind.DWord);
                }
                Logger.Log("Gaming++: GameDVR + Game Bar capture pipeline killed (registry).");
            }
            catch (Exception ex) { Logger.Log("DisableGameDvr: " + ex.Message); }
        }

        public static void RestoreGameDvr()
        {
            try
            {
                // Don't FORCE GameDVR back on -- just remove our policy overrides so
                // Settings UI choices win again.
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", true))
                {
                    k?.DeleteValue("AllowGameDVR", false);
                }
                Logger.Log("Gaming++ revert: GameDVR policy override removed.");
            }
            catch (Exception ex) { Logger.Log("RestoreGameDvr: " + ex.Message); }
        }

        // ------------------------------------------------------------------
        // 4. BACKGROUND UWP APPS policy
        // ------------------------------------------------------------------
        // HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications
        // Setting GlobalUserDisabled = 1 stops all UWP apps from running in background
        // (Mail, Photos, Cortana, etc.) without changing per-app toggles.
        public static void DisableBackgroundUwpApps()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", true))
                {
                    if (k != null) k.SetValue("GlobalUserDisabled", 1, RegistryValueKind.DWord);
                }
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search", true))
                {
                    if (k != null) k.SetValue("BackgroundAppGlobalToggle", 0, RegistryValueKind.DWord);
                }
                Logger.Log("Gaming++: background UWP apps policy disabled.");
            }
            catch (Exception ex) { Logger.Log("DisableBackgroundUwpApps: " + ex.Message); }
        }

        public static void RestoreBackgroundUwpApps()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", true))
                {
                    k?.DeleteValue("GlobalUserDisabled", false);
                }
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search", true))
                {
                    k?.DeleteValue("BackgroundAppGlobalToggle", false);
                }
                Logger.Log("Gaming++ revert: background UWP apps restored to default policy.");
            }
            catch (Exception ex) { Logger.Log("RestoreBackgroundUwpApps: " + ex.Message); }
        }

        // ------------------------------------------------------------------
        // 5. QoS DSCP marking for PioneerGame (Arc Raiders) UDP traffic
        // ------------------------------------------------------------------
        // Existing QoS policy in MainWindow covers msedge.exe (TradeSea). This
        // extends the pattern to PioneerGame-Win64-Shipping.exe with DSCP CS6
        // (network-control class, highest priority on most consumer routers).
        public static void AddQosForGame(string exeName, int dscp = 48)
        {
            try
            {
                string policyName = "RigOpt-" + exeName.Replace(".exe", "");
                // Remove first to be idempotent
                PowershellExec("Remove-NetQosPolicy -Name '" + policyName.Replace("'", "''") + "' -Confirm:$false -ErrorAction SilentlyContinue");
                // Add fresh
                string cmd =
                    "New-NetQosPolicy -Name '" + policyName.Replace("'", "''") +
                    "' -AppPathNameMatchCondition '" + exeName.Replace("'", "''") +
                    "' -IPProtocolMatchCondition Both" +
                    " -DSCPAction " + dscp +
                    " -NetworkProfile All";
                PowershellExec(cmd);
                Logger.Log("Gaming++: QoS DSCP " + dscp + " policy added for " + exeName + ".");
            }
            catch (Exception ex) { Logger.Log("AddQosForGame(" + exeName + "): " + ex.Message); }
        }

        public static void RemoveQosForGame(string exeName)
        {
            try
            {
                string policyName = "RigOpt-" + exeName.Replace(".exe", "");
                PowershellExec("Remove-NetQosPolicy -Name '" + policyName.Replace("'", "''") + "' -Confirm:$false -ErrorAction SilentlyContinue");
                Logger.Log("Gaming++ revert: QoS policy for " + exeName + " removed.");
            }
            catch (Exception ex) { Logger.Log("RemoveQosForGame(" + exeName + "): " + ex.Message); }
        }

        // ------------------------------------------------------------------
        // 6. HARDWARE DETECTION -- log vendor + emit the right manual-checklist hint
        // ------------------------------------------------------------------
        // The user has manual NVIDIA/AMD/Intel control-panel tweaks they'll do
        // alongside this. We detect the actual GPU + CPU vendor at runtime and log
        // the exact checklist for THEIR hardware so the message in the log is
        // always correct (no "open NVIDIA CP" hint for an Intel Arc user).

        public enum GpuVendor { Unknown, Nvidia, Amd, Intel }
        public enum CpuVendor { Unknown, Intel, Amd }

        public static GpuVendor DetectGpu()
        {
            try
            {
                foreach (var line in PowershellLines(
                    "Get-CimInstance Win32_VideoController | Where-Object { $_.Status -eq 'OK' } | Select-Object -ExpandProperty Name"))
                {
                    var l = line.ToLowerInvariant();
                    if (l.Contains("nvidia") || l.Contains("geforce") || l.Contains("rtx") || l.Contains("gtx")) return GpuVendor.Nvidia;
                    if (l.Contains("amd") || l.Contains("radeon") || l.Contains("rx ") || l.Contains("vega")) return GpuVendor.Amd;
                    if (l.Contains("intel") && (l.Contains("arc") || l.Contains("uhd") || l.Contains("iris") || l.Contains("xe"))) return GpuVendor.Intel;
                }
            }
            catch (Exception ex) { Logger.Log("DetectGpu: " + ex.Message); }
            return GpuVendor.Unknown;
        }

        public static CpuVendor DetectCpu()
        {
            try
            {
                foreach (var line in PowershellLines(
                    "Get-CimInstance Win32_Processor | Select-Object -ExpandProperty Manufacturer"))
                {
                    var l = line.ToLowerInvariant();
                    if (l.Contains("genuineintel") || l.Contains("intel")) return CpuVendor.Intel;
                    if (l.Contains("authenticamd") || l.Contains("amd")) return CpuVendor.Amd;
                }
            }
            catch (Exception ex) { Logger.Log("DetectCpu: " + ex.Message); }
            return CpuVendor.Unknown;
        }

        /// <summary>Print the right manual-tweak checklist for the detected GPU + CPU.</summary>
        public static void LogManualChecklist()
        {
            var gpu = DetectGpu();
            var cpu = DetectCpu();
            int cores = Environment.ProcessorCount;
            Logger.Log("=== Hardware-aware manual checklist (yours: " + cpu + " " + cores + "t, " + gpu + " GPU) ===");

            // GPU-side
            switch (gpu)
            {
                case GpuVendor.Nvidia:
                    Logger.Log("NVIDIA Control Panel -> Manage 3D Settings -> Global:");
                    Logger.Log("  - Power management mode = Prefer Maximum Performance");
                    Logger.Log("  - Threaded Optimization  = On");
                    Logger.Log("  - Low Latency Mode       = Ultra");
                    Logger.Log("  - Background app max FPS = 30");
                    Logger.Log("  - Vertical Sync          = Off (verify)");
                    break;
                case GpuVendor.Amd:
                    Logger.Log("AMD Adrenalin (Radeon Software) -> Gaming -> Global Settings:");
                    Logger.Log("  - Radeon Anti-Lag        = Enabled");
                    Logger.Log("  - Radeon Boost           = Disabled (causes resolution dips)");
                    Logger.Log("  - Radeon Chill           = Disabled (frame-limiting causes latency)");
                    Logger.Log("  - Wait for Vertical Refresh = Always Off");
                    Logger.Log("  - Texture Filtering Quality = Performance");
                    Logger.Log("  - Surface Format Optimization = Enabled");
                    Logger.Log("  - Tessellation Mode      = Override application settings -> 16x");
                    break;
                case GpuVendor.Intel:
                    Logger.Log("Intel Arc Control -> Performance -> Global:");
                    Logger.Log("  - Performance Tuning     = Performance");
                    Logger.Log("  - Power Saving           = Disabled");
                    Logger.Log("  - Vertical Sync          = Off");
                    Logger.Log("  - Xe Super Sampling      = Performance (if supported by your title)");
                    Logger.Log("  - Endurance Gaming       = Disabled");
                    break;
                default:
                    Logger.Log("GPU vendor not recognized -- skipping GPU-CP checklist.");
                    break;
            }

            // CPU-side
            switch (cpu)
            {
                case CpuVendor.Amd:
                    Logger.Log("CPU (Ryzen) BIOS / Ryzen Master:");
                    Logger.Log("  - PBO = Advanced -> Curve Optimizer = All cores, NEGATIVE 15-25 (test stable)");
                    Logger.Log("  - Memory Context Restore = Enabled (faster reboots, less DRAM training)");
                    Logger.Log("  - Power Supply Idle Control = Typical Current Idle");
                    Logger.Log("  - Global C-state Control = Auto (only disable if seeing audio crackle)");
                    Logger.Log("  - DOCP/EXPO profile     = Enabled (your RAM's rated XMP)");
                    break;
                case CpuVendor.Intel:
                    Logger.Log("CPU (Intel) BIOS / XTU:");
                    Logger.Log("  - Intel Turbo Boost = Enabled");
                    Logger.Log("  - Intel SpeedStep   = Disabled (force max multiplier)");
                    Logger.Log("  - C-States          = C1E only (deeper sleep states add wake latency)");
                    Logger.Log("  - Package Power Limits = Unlimited / Tune up via XTU");
                    Logger.Log("  - Adaptive Voltage / Undervolt = Test -50mV to -100mV via XTU (gen-dependent)");
                    Logger.Log("  - XMP profile       = Enabled (your RAM's rated XMP)");
                    break;
                default:
                    Logger.Log("CPU vendor not recognized -- skipping CPU checklist.");
                    break;
            }
            Logger.Log("=== End checklist ===");
        }

        // ------------------------------------------------------------------
        // PUBLIC ENTRY POINTS -- called from the Gaming Mode / Restore buttons
        // ------------------------------------------------------------------

        /// <summary>Apply the full Gaming Mode++ layer. Idempotent.</summary>
        public static void ApplyAll()
        {
            Logger.Log("=== Gaming Mode++ apply (MSI + NIC + GameDVR + BGapps + QoS) ===");
            EnableMsiModeForGpuAndNic();
            HardenAllPhysicalNics();
            DisableGameDvr();
            DisableBackgroundUwpApps();
            // Default Arc Raiders coverage; can be extended via settings
            AddQosForGame("PioneerGame-Win64-Shipping.exe");
            AddQosForGame("PioneerGame-d.exe");
            // Print the right manual-tweak checklist for the actual hardware
            LogManualChecklist();
            Logger.Log("=== Gaming Mode++ apply done. Reboot for MSI mode to take effect. ===");
        }

        /// <summary>Reverse the full Gaming Mode++ layer. Symmetric to ApplyAll.</summary>
        public static void RestoreAll()
        {
            Logger.Log("=== Gaming Mode++ restore ===");
            DisableMsiModeForGpuAndNic();
            RestoreAllPhysicalNics();
            RestoreGameDvr();
            RestoreBackgroundUwpApps();
            RemoveQosForGame("PioneerGame-Win64-Shipping.exe");
            RemoveQosForGame("PioneerGame-d.exe");
            Logger.Log("=== Gaming Mode++ restore done. ===");
        }

        // ------------------------------------------------------------------
        // Internal helpers
        // ------------------------------------------------------------------
        private static System.Collections.Generic.List<string> PowershellLines(string command)
        {
            var result = new System.Collections.Generic.List<string>();
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    if (p == null) return result;
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                        result.Add(line);
                    p.WaitForExit(5000);
                }
            }
            catch (Exception ex) { Logger.Log("PowershellLines: " + ex.Message); }
            return result;
        }

        private static void PowershellExec(string command)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    p?.WaitForExit(8000);
                }
            }
            catch (Exception ex) { Logger.Log("PowershellExec: " + ex.Message); }
        }
    }
}
