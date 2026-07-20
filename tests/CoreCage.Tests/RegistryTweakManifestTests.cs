using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;
using CoreCage.Core;

namespace CoreCage.Tests
{
    /// <summary>
    /// Guards the snapshot-before-write safety net. The manifest is the single source of truth for
    /// every registry value the apply paths mutate; if it drifts, the Big Red Button stops restoring
    /// the user's true originals. The round-trip test exercises the real RegistryBackup plumbing that
    /// was previously dead code (Snapshot was never called; restore read the wrong directory).
    /// </summary>
    [TestClass]
    public class RegistryTweakManifestTests
    {
        [TestMethod]
        public void Targets_AreNonEmpty()
        {
            Assert.IsTrue(RegistryTweakManifest.Targets.Count >= 25,
                $"Expected the manifest to cover the full apply surface; got {RegistryTweakManifest.Targets.Count}.");
        }

        [TestMethod]
        public void Targets_UseOnlyKnownHives()
        {
            var known = new HashSet<string> { "HKLM", "HKCU", "HKCR", "HKU", "HKCC" };
            foreach (var t in RegistryTweakManifest.Targets)
                Assert.IsTrue(known.Contains(t.hive), $"Unknown hive '{t.hive}' for {t.subKey}\\{t.name}");
        }

        [TestMethod]
        public void Targets_HaveNoBlankSubKeyOrName()
        {
            foreach (var t in RegistryTweakManifest.Targets)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(t.subKey), "subKey must not be blank");
                Assert.IsFalse(string.IsNullOrWhiteSpace(t.name), $"name must not be blank under {t.subKey}");
            }
        }

        [TestMethod]
        public void Targets_HaveNoDuplicates()
        {
            var dupes = RegistryTweakManifest.Targets
                .GroupBy(t => $"{t.hive}|{t.subKey}|{t.name}")
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            Assert.AreEqual(0, dupes.Count, "Duplicate targets: " + string.Join(", ", dupes));
        }

        [TestMethod]
        public void Targets_CoverTheCriticalRegressionKeys()
        {
            // If any apply path drops these, the snapshot would no longer protect them — fail loudly.
            void Require(string subKeyContains, string name) =>
                Assert.IsTrue(
                    RegistryTweakManifest.Targets.Any(t => t.subKey.Contains(subKeyContains) && t.name == name),
                    $"Manifest is missing {subKeyContains} :: {name}");

            Require("SystemProfile", "NetworkThrottlingIndex");
            Require("SystemProfile", "SystemResponsiveness");
            Require(@"Tasks\Games", "GPU Priority");
            Require("Tcpip", "MaxUserPort");
            Require("GraphicsDrivers", "HwSchMode");
            Require("PriorityControl", "Win32PrioritySeparation");
        }

        [TestMethod]
        public void SnapshotLabel_HasCoreCagePrefix_SoBigRedButtonFindsIt()
        {
            // RestoreEverything sweeps "corecage-*"; a label without that prefix would never be restored.
            StringAssert.StartsWith(RegistryTweakManifest.SnapshotLabel, "corecage-");
        }

        [TestMethod]
        public void RegistryBackup_RoundTrips_ChangedAndAbsentValues_ThenPrefixRestoreReverts()
        {
            // Real plumbing test against a throwaway HKCU key (no admin, self-cleaning). Verifies the
            // exact contract the Big Red Button relies on: a changed value is restored to its original,
            // and a value that did NOT exist at snapshot time is deleted on restore.
            const string subKey = @"Software\CoreCage\__manifest_roundtrip_test";
            const string label = "corecage-unit-roundtrip";
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(subKey, true))
                {
                    k.SetValue("Existing", 111, RegistryValueKind.DWord);   // original value
                    k.DeleteValue("Added", false);                          // ensure it's absent at snapshot
                }

                RegistryBackup.Snapshot(label, new[]
                {
                    ("HKCU", subKey, "Existing"),
                    ("HKCU", subKey, "Added"),
                });
                Assert.IsTrue(RegistryBackup.HasSnapshot(label));

                // Mutate like an apply path would.
                using (var k = Registry.CurrentUser.OpenSubKey(subKey, true))
                {
                    k!.SetValue("Existing", 999, RegistryValueKind.DWord);
                    k.SetValue("Added", 1, RegistryValueKind.DWord);
                }

                int restored = RegistryBackup.RestoreAllWithPrefix("corecage-unit-");
                Assert.IsTrue(restored >= 1, "Prefix restore should have restored the snapshot");

                using (var k = Registry.CurrentUser.OpenSubKey(subKey, false))
                {
                    Assert.AreEqual(111, (int)k!.GetValue("Existing"), "changed value must revert to original");
                    Assert.IsNull(k.GetValue("Added"), "value absent at snapshot must be deleted on restore");
                }
            }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(subKey, false); } catch { }
                try
                {
                    string f = System.IO.Path.Combine(RegistryBackup.BackupDirectory, label + ".json");
                    if (System.IO.File.Exists(f)) System.IO.File.Delete(f);
                }
                catch { }
            }
        }
    }
}
