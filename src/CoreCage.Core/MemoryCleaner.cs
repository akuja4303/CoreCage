using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace CoreCage.Core
{
    /// <summary>
    /// Provides memory cleaning and optimization functions.
    /// Requires administrator privileges for full functionality.
    /// </summary>
    public static class MemoryCleaner
    {
        // P/Invoke declarations for memory management
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("ntdll.dll")]
        private static extern int NtSetInformationProcess(
            IntPtr hProcess,
            int processInformationClass,
            ref int processInformation,
            int processInformationLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr TokenHandle,
            bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState,
            int BufferLength,
            IntPtr PreviousState,
            IntPtr ReturnLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, int DesiredAccess, out IntPtr TokenHandle);

        // Constants
        private const int PROCESS_SET_INFORMATION = 0x0202;
        private const int PROCESS_QUOTA_LIMITS = 0x0200;
        private const int PROCESS_EMPTY_WORKING_SET = 0x0800;
        private const int SE_PRIVILEGE_ENABLED = 0x00000002;
        private const int TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const int TOKEN_QUERY = 0x0008;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public int LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privileges;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public int Attributes;
        }

        private static bool _privilegeEnabled = false;

        /// <summary>
        /// Enables the required privileges for memory operations.
        /// </summary>
        private static bool EnablePrivileges()
        {
            try
            {
                Logger.Log("PURGE: Enabling SeDebug privilege...");
                
                IntPtr tokenHandle;
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out tokenHandle))
                {
                    Logger.Log($"PURGE: OpenProcessToken failed - {new Win32Exception().Message}");
                    return false;
                }

                // Enable SE_DEBUG_PRIVILEGE
                if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out LUID luid))
                {
                    Logger.Log("PURGE: LookupPrivilegeValue failed for SeDebugPrivilege");
                    return false;
                }

                TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES();
                tp.PrivilegeCount = 1;
                tp.Privileges = new LUID_AND_ATTRIBUTES();
                tp.Privileges.Luid = luid;
                tp.Privileges.Attributes = SE_PRIVILEGE_ENABLED;

                if (!AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                {
                    Logger.Log($"PURGE: AdjustTokenPrivileges failed - {new Win32Exception().Message}");
                    return false;
                }

                _privilegeEnabled = true;
                Logger.Log("PURGE: Privilege enabled successfully!");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("PURGE: EnablePrivileges failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Performs a full memory purge by forcing working set reduction.
        /// Requires administrator privileges.
        /// </summary>
        public static void Purge()
        {
            try
            {
                Logger.Log("PURGE: Starting memory purge...");
                
                // Enable privileges
                if (!_privilegeEnabled)
                {
                    EnablePrivileges();
                }

                Logger.Log("PURGE: Starting full memory purge...");
                
                // Force garbage collection first
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // Build the skip-set: CoreCage itself + the active foreground game.
                // Trimming the game's working set causes page-fault stalls mid-frame (hitches).
                int selfPid = System.Diagnostics.Process.GetCurrentProcess().Id;
                string? activeGame = ProcessWatcher.GetActiveGameProcessName(); // may be null

                // Get all processes and empty their working sets
                int purgedCount = 0;
                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        // Skip kernel/system pseudo-processes
                        if (process.Id == 0 || process.Id == 4) continue;

                        // Skip CoreCage itself — trimming our own WS hurts nothing but
                        // it's wasteful and can cause GC pressure mid-purge.
                        if (process.Id == selfPid) continue;

                        // Skip the foreground game by PID name match.
                        // Also skip by the known game exe fragment (PioneerGame-d / Arc Raiders)
                        // in case _activeGameProcessName isn't set yet.
                        if (activeGame != null &&
                            string.Equals(process.ProcessName, activeGame, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Belt-and-suspenders: never trim a process whose name looks like
                        // the Arc Raiders executable regardless of watcher state.
                        if (process.ProcessName.IndexOf("PioneerGame", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;

                        // Try to empty working set (requires admin)
                        try
                        {
                            if (EmptyWorkingSet(process.Handle))
                            {
                                purgedCount++;
                            }
                        }
                        catch
                        {
                            // Some processes may throw access denied
                        }

                        // Also try SetProcessWorkingSetSize
                        try
                        {
                            SetProcessWorkingSetSize(process.Handle, -1, -1);
                        }
                        catch
                        {
                            // Ignore errors for protected processes
                        }
                    }
                    catch
                    {
                        // Skip processes that can't be accessed
                    }
                }

                Logger.Log($"PURGE: Purged {purgedCount} process working sets");

                // Final garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                
                Logger.Log("PURGE: Memory purge completed.");
            }
            catch (Exception ex)
            {
                Logger.LogError("PURGE: Full purge failed", ex);
                throw;
            }
        }

        /// <summary>
        /// Performs a gentle purge by clearing the standby list.
        /// Uses NtSetSystemInformation for actual standby list clearing.
        /// </summary>
        public static void PurgeStandbyList()
        {
            try
            {
                Logger.Log("PURGE: Starting gentle purge (standby list)...");
                
                // Enable privileges first
                if (!_privilegeEnabled)
                {
                    EnablePrivileges();
                }

                // Force garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                // Try to minimize current process memory
                try
                {
                    var currentProcess = Process.GetCurrentProcess();
                    EmptyWorkingSet(currentProcess.Handle);
                    SetProcessWorkingSetSize(currentProcess.Handle, -1, -1);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Could not minimize current process: {ex.Message}");
                }

                // Actually clear the standby list (the WS trim above does NOT do this).
                bool standbyOk = Memory.StandbyListCleaner.PurgeStandbyList();
                Logger.Log(standbyOk
                    ? "PURGE: Gentle purge completed - standby list cleared"
                    : "PURGE: Gentle purge completed - working sets trimmed (standby purge unavailable)");
            }
            catch (Exception ex)
            {
                Logger.LogError("PURGE: Gentle purge failed", ex);
                throw;
            }
        }

        /// <summary>
        /// Gets the current available RAM in bytes.
        /// </summary>
        public static long GetAvailableRAM()
        {
            return SystemMonitor.GetAvailableRAM();
        }

        /// <summary>
        /// Gets the total RAM in bytes.
        /// </summary>
        public static long GetTotalRAM()
        {
            return SystemMonitor.GetTotalRAM();
        }

        /// <summary>
        /// Formats bytes to human readable string.
        /// </summary>
        public static string FormatBytes(long bytes)
        {
            return SystemMonitor.FormatBytes(bytes);
        }
    }
}
