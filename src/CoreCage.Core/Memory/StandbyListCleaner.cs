using System;
using System.Runtime.InteropServices;

namespace CoreCage.Core.Memory
{
    /// <summary>
    /// Real Windows standby-list purge (the ISLC capability). Clears the file-cache standby pages via
    /// <c>NtSetSystemInformation(SystemMemoryListInformation, MemoryPurgeStandbyList)</c> — which needs
    /// <c>SeProfileSingleProcessPrivilege</c> (the app runs elevated). This frees RAM the OS was holding
    /// as cache, reducing the stutter that standby-list bloat causes in long gaming sessions. All calls
    /// are best-effort + logged; reads return -1 ("unknown") on any doubt so callers fall back safely.
    /// </summary>
    public static class StandbyListCleaner
    {
        public static bool PurgeStandbyList()
        {
            try
            {
                if (!EnableProfilePrivilege())
                {
                    Logger.Log("Standby purge: could not enable SeProfileSingleProcessPrivilege");
                    return false;
                }
                int command = MemoryPurgeStandbyList;
                uint status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
                if (status != 0)
                {
                    Logger.Log($"Standby purge: NtSetSystemInformation returned 0x{status:X8}");
                    return false;
                }
                Logger.Log("Standby list purged");
                return true;
            }
            catch (Exception ex) { Logger.LogError("Standby purge failed", ex); return false; }
        }

        /// <summary>Available physical RAM in MB (reuses SystemMonitor), or -1.</summary>
        public static long GetAvailableMb()
        {
            try { return SystemMonitor.GetAvailableRAM() / (1024 * 1024); } catch { return -1; }
        }

        /// <summary>Standby-list size in MB (sum of PageCountByPriority), or -1 if unavailable/insane.</summary>
        public static long GetStandbyMb()
        {
            IntPtr buf = IntPtr.Zero;
            try
            {
                int size = Marshal.SizeOf<SYSTEM_MEMORY_LIST_INFORMATION>();
                buf = Marshal.AllocHGlobal(size);
                uint status = NtQuerySystemInformation(SystemMemoryListInformation, buf, size, out _);
                if (status != 0) return -1;

                var info = Marshal.PtrToStructure<SYSTEM_MEMORY_LIST_INFORMATION>(buf);
                if (info.PageCountByPriority == null) return -1;
                ulong pages = 0;
                foreach (UIntPtr p in info.PageCountByPriority) pages += p.ToUInt64();

                long mb = (long)(pages * PageSize / (1024UL * 1024UL));
                long totalMb = 0;
                try { totalMb = SystemMonitor.GetTotalRAM() / (1024 * 1024); } catch { }
                if (mb < 0 || (totalMb > 0 && mb > totalMb)) return -1; // sanity guard against bad marshaling
                return mb;
            }
            catch { return -1; }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        }

        /// <summary>Reads memory state, applies the policy, and purges if warranted. Returns true if purged.</summary>
        public static bool PurgeIfNeeded(StandbyCleanerPolicy policy)
        {
            if (policy == null) return false;
            long free = GetAvailableMb();
            long standby = GetStandbyMb();
            if (!policy.ShouldPurge(free, standby)) return false;
            bool ok = PurgeStandbyList();
            Logger.Event("Standby auto-purge (free={Free}MB standby={Standby}MB) → {Ok}", free, standby, ok);
            return ok;
        }

        // ── Privilege ──────────────────────────────────────────────────────────
        private static bool _privEnabled;

        private static bool EnableProfilePrivilege()
        {
            if (_privEnabled) return true;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token))
                return false;
            try
            {
                if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out LUID luid)) return false;
                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privilege = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED },
                };
                if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) return false;
                _privEnabled = true;
                return true;
            }
            finally { CloseHandle(token); }
        }

        // ── Win32 / NT ─────────────────────────────────────────────────────────
        private const int SystemMemoryListInformation = 0x0050; // 80
        private const int MemoryPurgeStandbyList = 4;
        private const int SE_PRIVILEGE_ENABLED = 0x0002;
        private const int TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const int TOKEN_QUERY = 0x0008;
        private const ulong PageSize = 4096;

        [DllImport("ntdll.dll")]
        private static extern uint NtSetSystemInformation(int infoClass, ref int info, int length);

        [DllImport("ntdll.dll")]
        private static extern uint NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);

        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string? host, string name, out LUID luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll,
            ref TOKEN_PRIVILEGES newState, int length, IntPtr previous, IntPtr returnLength);

        [StructLayout(LayoutKind.Sequential)] private struct LUID { public int LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential)] private struct LUID_AND_ATTRIBUTES { public LUID Luid; public int Attributes; }
        [StructLayout(LayoutKind.Sequential)] private struct TOKEN_PRIVILEGES { public int PrivilegeCount; public LUID_AND_ATTRIBUTES Privilege; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_MEMORY_LIST_INFORMATION
        {
            public UIntPtr ZeroPageCount;
            public UIntPtr FreePageCount;
            public UIntPtr ModifiedPageCount;
            public UIntPtr ModifiedNoWritePageCount;
            public UIntPtr BadPageCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public UIntPtr[] PageCountByPriority;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public UIntPtr[] RepurposedByPriority;
            public UIntPtr ModifiedPageCountPageFile;
        }
    }
}
