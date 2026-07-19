using System;
using System.Management;

namespace CoreCage.Core
{
    /// <summary>
    /// Creates a Windows System Restore Point before CoreCage makes system-level changes, so a
    /// user can roll the machine back if a tweak destabilises it. Best-effort: System Restore must be
    /// enabled on the system drive, requires admin (the app runs elevated), and Windows throttles
    /// creation to once per 24h by default (SystemRestorePointCreationFrequency) — all handled
    /// gracefully (logged, never throws).
    /// </summary>
    public static class RestorePoint
    {
        // RestorePointType / EventType constants (see CreateRestorePoint, SystemRestore WMI class).
        private const uint APPLICATION_INSTALL = 0;
        private const uint MODIFY_SETTINGS     = 12;
        private const uint BEGIN_SYSTEM_CHANGE = 100;
        private const uint END_SYSTEM_CHANGE   = 101;

        /// <summary>
        /// Attempts to create a restore point. Returns true on success. Non-blocking callers should
        /// use <see cref="CreateAsync"/> so a slow VSS snapshot never stalls the UI.
        /// </summary>
        public static bool Create(string description)
        {
            try
            {
                using var mc = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
                ManagementBaseObject inParams = mc.GetMethodParameters("CreateRestorePoint");
                inParams["Description"]      = description;
                inParams["RestorePointType"] = MODIFY_SETTINGS;
                inParams["EventType"]        = BEGIN_SYSTEM_CHANGE;

                ManagementBaseObject outParams = mc.InvokeMethod("CreateRestorePoint", inParams, null);
                uint ret = Convert.ToUInt32(outParams?["ReturnValue"] ?? 1u);

                if (ret == 0)
                {
                    Logger.Log($"Restore point created: \"{description}\"");
                    return true;
                }

                // 1058 = service disabled; 0x80070422 paths surface as non-zero here too.
                Logger.Log($"Restore point not created (WMI ReturnValue={ret}) — System Restore may be " +
                           "disabled, or one was already made in the last 24h.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError("Restore point creation failed", ex);
                return false;
            }
        }

        /// <summary>Fire-and-forget restore point so mode switches aren't blocked by the VSS snapshot.</summary>
        public static void CreateAsync(string description)
        {
            System.Threading.Tasks.Task.Run(() => Create(description));
        }
    }
}
