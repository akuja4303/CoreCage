using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace CoreCage.Core
{
    public class StartupEntry
    {
        public string Name           { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string Source         { get; set; } = ""; // "HKLM", "HKCU", "Startup Folder"
        public bool   IsEnabled      { get; set; }
        public string Recommendation { get; set; } = "Safe"; // "Safe" | "Optional" | "Bloatware"

        // Registry-backed entries store the key + value name for enable/disable
        internal string? RegistryRoot   { get; set; } // "HKLM" or "HKCU"
        internal string? RegistryPath   { get; set; }
        internal string? RegistryValue  { get; set; }
        internal string? DisabledRegPath { get; set; }

        // Startup folder entries store the full file path
        internal string? FilePath { get; set; }
    }

    public static class StartupManager
    {
        // ── Registry paths ────────────────────────────────────────────────────
        private const string RunPath         = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string RunOncePath     = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
        // Disabled entries are stored here by Task Manager / other tools
        private const string DisabledRunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        // ── Classification lists ──────────────────────────────────────────────
        private static readonly HashSet<string> BloatwareKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mcafee", "norton", "avast", "avg", "malwarebytes",
            "driver booster", "driver easy", "ccleaner", "advanced systemcare",
            "iobit", "pc optimizer", "reimage",
            "onedrive", "skype", "teams", "cortana",
            "spotify", "acrobat", "adobe updater", "adobe arm",
            "quicktime", "real player",
            "manufacturer updater", "hp notification", "dell",
            "lenovo", "asus", "acer", "toshiba",
        };

        private static readonly HashSet<string> OptionalKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "steam", "epicgameslauncher", "gog", "ubisoft", "battle.net",
            "discord", "slack", "zoom", "telegram", "signal", "whatsapp",
            "obs", "streamlabs",
            "geforce experience", "amd software", "radeon",
            "logitech", "razer", "corsair", "steelseries",
            "chrome", "firefox", "edge", "brave",
        };

        // ── Public API ────────────────────────────────────────────────────────
        public static List<StartupEntry> GetStartupEntries()
        {
            var entries = new List<StartupEntry>();
            ReadRegistryRun(entries, Registry.LocalMachine, RunPath,     "HKLM");
            ReadRegistryRun(entries, Registry.CurrentUser,  RunPath,     "HKCU");
            ReadRegistryRun(entries, Registry.LocalMachine, RunOncePath, "HKLM (RunOnce)");
            ReadRegistryRun(entries, Registry.CurrentUser,  RunOncePath, "HKCU (RunOnce)");
            ReadStartupFolder(entries);
            return entries;
        }

        public static void SetEnabled(StartupEntry entry, bool enable)
        {
            if (entry.FilePath != null)
                SetEnabledFolder(entry, enable);
            else
                SetEnabledRegistry(entry, enable);
        }

        // ── Registry reads ────────────────────────────────────────────────────
        private static void ReadRegistryRun(List<StartupEntry> entries, RegistryKey root,
                                            string path, string sourceName)
        {
            try
            {
                using var key = root.OpenSubKey(path, false);
                if (key == null) return;

                // Load the StartupApproved disabled map for this root
                var disabledValues = GetDisabledMap(root);

                foreach (var valueName in key.GetValueNames())
                {
                    try
                    {
                        string rawValue = key.GetValue(valueName)?.ToString() ?? "";
                        string exePath  = ExtractExePath(rawValue);

                        bool isEnabled = !disabledValues.Contains(valueName);

                        var entry = new StartupEntry
                        {
                            Name           = valueName,
                            ExecutablePath = exePath,
                            Source         = sourceName,
                            IsEnabled      = isEnabled,
                            Recommendation = Classify(valueName, exePath),
                            RegistryRoot   = sourceName.StartsWith("HKLM") ? "HKLM" : "HKCU",
                            RegistryPath   = path,
                            RegistryValue  = valueName,
                            DisabledRegPath = DisabledRunPath,
                        };
                        entries.Add(entry);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static HashSet<string> GetDisabledMap(RegistryKey root)
        {
            var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var approvedKey = root.OpenSubKey(DisabledRunPath, false);
                if (approvedKey == null) return disabled;

                foreach (var name in approvedKey.GetValueNames())
                {
                    var data = approvedKey.GetValue(name) as byte[];
                    // First byte 0x03 = disabled, 0x02 = enabled (Task Manager format)
                    if (data != null && data.Length > 0 && data[0] == 0x03)
                        disabled.Add(name);
                }
            }
            catch { }
            return disabled;
        }

        // ── Startup folder ────────────────────────────────────────────────────
        private static void ReadStartupFolder(List<StartupEntry> entries)
        {
            ReadFolder(entries,
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                "Startup Folder (User)");

            ReadFolder(entries,
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                "Startup Folder (All Users)");
        }

        private static void ReadFolder(List<StartupEntry> entries, string folder, string sourceName)
        {
            if (!Directory.Exists(folder)) return;
            try
            {
                foreach (var file in Directory.GetFiles(folder, "*.lnk"))
                {
                    string name    = Path.GetFileNameWithoutExtension(file);
                    string target  = ResolveLnkTarget(file);
                    entries.Add(new StartupEntry
                    {
                        Name           = name,
                        ExecutablePath = target,
                        Source         = sourceName,
                        IsEnabled      = true,
                        Recommendation = Classify(name, target),
                        FilePath       = file,
                    });
                }
            }
            catch { }
        }

        // ── Enable / Disable ─────────────────────────────────────────────────
        private static void SetEnabledRegistry(StartupEntry entry, bool enable)
        {
            try
            {
                var root = entry.RegistryRoot == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;

                using var approvedKey = root.OpenSubKey(DisabledRunPath, true)
                                     ?? root.CreateSubKey(DisabledRunPath);
                if (approvedKey == null) return;

                // 0x02 00 00 00 00 00 00 00 00 00 00 00 = enabled
                // 0x03 00 00 00 00 00 00 00 00 00 00 00 = disabled
                var data = new byte[12];
                data[0] = enable ? (byte)0x02 : (byte)0x03;
                approvedKey.SetValue(entry.RegistryValue!, data, RegistryValueKind.Binary);

                entry.IsEnabled = enable;
                Logger.Log($"Startup entry '{entry.Name}' {(enable ? "enabled" : "disabled")}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"SetEnabledRegistry failed for '{entry.Name}'", ex);
            }
        }

        private static void SetEnabledFolder(StartupEntry entry, bool enable)
        {
            if (entry.FilePath == null) return;
            try
            {
                string disabledPath = entry.FilePath + ".disabled";
                if (!enable && File.Exists(entry.FilePath))
                {
                    File.Move(entry.FilePath, disabledPath);
                    entry.FilePath  = disabledPath;
                    entry.IsEnabled = false;
                    Logger.Log($"Startup entry '{entry.Name}' disabled (renamed .disabled)");
                }
                else if (enable && File.Exists(disabledPath))
                {
                    File.Move(disabledPath, entry.FilePath!.Replace(".disabled", ""));
                    entry.FilePath  = entry.FilePath.Replace(".disabled", "");
                    entry.IsEnabled = true;
                    Logger.Log($"Startup entry '{entry.Name}' enabled");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"SetEnabledFolder failed for '{entry.Name}'", ex);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static string Classify(string name, string path)
        {
            string combined = (name + " " + path).ToLowerInvariant();

            foreach (var kw in BloatwareKeywords)
                if (combined.Contains(kw.ToLowerInvariant()))
                    return "Bloatware";

            foreach (var kw in OptionalKeywords)
                if (combined.Contains(kw.ToLowerInvariant()))
                    return "Optional";

            return "Safe";
        }

        private static string ExtractExePath(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue)) return rawValue;
            rawValue = rawValue.Trim();

            // Handle quoted paths: "C:\Program Files\..." [args]
            if (rawValue.StartsWith("\""))
            {
                int close = rawValue.IndexOf('"', 1);
                return close > 0 ? rawValue.Substring(1, close - 1) : rawValue;
            }

            // Unquoted: take up to first space
            int space = rawValue.IndexOf(' ');
            return space > 0 ? rawValue.Substring(0, space) : rawValue;
        }

        private static string ResolveLnkTarget(string lnkPath)
        {
            // WScript.Shell can resolve .lnk targets but requires COM. Fall back to path display.
            try
            {
                Type? t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return lnkPath;
                dynamic shell    = Activator.CreateInstance(t)!;
                dynamic shortcut = shell.CreateShortcut(lnkPath);
                return shortcut.TargetPath ?? lnkPath;
            }
            catch { return lnkPath; }
        }
    }
}
