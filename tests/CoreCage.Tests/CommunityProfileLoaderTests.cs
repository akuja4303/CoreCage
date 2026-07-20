using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Detection;
using CoreCage.Core.Profiles;

namespace CoreCage.Tests
{
    /// <summary>
    /// Task 7: community-submitted (PR-able) game profile format + loader. Covers the loader's
    /// resilience contract (malformed file -> per-file error, valid files still load; missing/empty
    /// dir -> empty, never throws), the real profiles\example-arc-raiders.json actually parsing, and
    /// the pure auto-apply match function that gates on the existing confidence classifier.
    /// </summary>
    [TestClass]
    public class CommunityProfileLoaderTests
    {
        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "CoreCageCommunityProfileTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Teardown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        private void WriteFile(string name, string content) =>
            File.WriteAllText(Path.Combine(_tempDir, name), content);

        // ── Repo-root discovery for reading the real profiles\ folder ──────────────────────────
        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CoreCage.sln")))
                dir = dir.Parent;
            Assert.IsNotNull(dir, "Could not locate CoreCage.sln walking up from " + AppContext.BaseDirectory);
            return dir!.FullName;
        }

        // ── Loads the real example ──────────────────────────────────────────────────────────────
        [TestMethod]
        public void LoadDirectory_LoadsRealExampleArcRaidersProfile()
        {
            string profilesDir = Path.Combine(FindRepoRoot(), "profiles");
            Assert.IsTrue(Directory.Exists(profilesDir), $"Expected {profilesDir} to exist.");

            CommunityProfileLoadResult result = CommunityProfileLoader.LoadDirectory(profilesDir);

            Assert.AreEqual(0, result.Errors.Count, "example-arc-raiders.json must parse cleanly: " +
                string.Join("; ", result.Errors.Select(e => $"{e.FilePath}: {e.Message}")));

            CommunityProfileEntry? entry = result.Profiles.FirstOrDefault(
                e => e.Profile.ExeName.Equals("PioneerGame-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(entry, "example-arc-raiders.json should have loaded a profile for the Pioneer exe.");

            Assert.AreEqual("Arc Raiders", entry!.Profile.DisplayName);
            Assert.AreEqual("PioneerGame-Win64-Shipping.exe", entry.Profile.ExeName);
            Assert.AreEqual("High", entry.Profile.Priority);
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 5 }, entry.Profile.ReservedCores);
            CollectionAssert.Contains((System.Collections.ICollection)entry.Tweaks, "gaming-stack");
            Assert.IsNotNull(entry.SubmittedBenchmark);
            Assert.AreEqual(150, entry.SubmittedBenchmark!.Fps);
            Assert.AreEqual(85, entry.SubmittedBenchmark.OnePctLow);
            Assert.AreEqual("Ryzen 5 5600G / RTX 3060 / 64GB", entry.SubmittedBenchmark.Rig);
        }

        // ── Malformed + valid mix ────────────────────────────────────────────────────────────────
        [TestMethod]
        public void LoadDirectory_MalformedFile_ReportedInErrors_ValidFilesStillLoad()
        {
            WriteFile("valid-game.json", @"{
                ""game"": ""Valid Game"",
                ""exe"": ""validgame.exe"",
                ""priority"": ""AboveNormal""
            }");
            WriteFile("broken-game.json", "{ this is not valid json ][");

            CommunityProfileLoadResult result = CommunityProfileLoader.LoadDirectory(_tempDir);

            Assert.AreEqual(1, result.Profiles.Count);
            Assert.AreEqual("validgame.exe", result.Profiles[0].Profile.ExeName);

            Assert.AreEqual(1, result.Errors.Count);
            StringAssert.Contains(result.Errors[0].FilePath, "broken-game.json");
        }

        [TestMethod]
        public void LoadDirectory_MissingRequiredExeField_ReportedInErrors()
        {
            WriteFile("no-exe.json", @"{ ""game"": ""No Exe Game"" }");

            CommunityProfileLoadResult result = CommunityProfileLoader.LoadDirectory(_tempDir);

            Assert.AreEqual(0, result.Profiles.Count);
            Assert.AreEqual(1, result.Errors.Count);
            StringAssert.Contains(result.Errors[0].Message, "exe");
        }

        // ── Empty / missing dir ──────────────────────────────────────────────────────────────────
        [TestMethod]
        public void LoadDirectory_EmptyDirectory_ReturnsEmpty_NoThrow()
        {
            CommunityProfileLoadResult result = CommunityProfileLoader.LoadDirectory(_tempDir);

            Assert.AreEqual(0, result.Profiles.Count);
            Assert.AreEqual(0, result.Errors.Count);
        }

        [TestMethod]
        public void LoadDirectory_MissingDirectory_ReturnsEmpty_NoThrow()
        {
            string missing = Path.Combine(_tempDir, "does-not-exist-" + Guid.NewGuid());

            CommunityProfileLoadResult result = CommunityProfileLoader.LoadDirectory(missing);

            Assert.AreEqual(0, result.Profiles.Count);
            Assert.AreEqual(0, result.Errors.Count);
        }

        [TestMethod]
        public void LoadDirectory_NullOrWhitespacePath_ReturnsEmpty_NoThrow()
        {
            Assert.AreEqual(0, CommunityProfileLoader.LoadDirectory(null!).Profiles.Count);
            Assert.AreEqual(0, CommunityProfileLoader.LoadDirectory("   ").Profiles.Count);
        }

        // ── Defaults when optional fields are omitted ───────────────────────────────────────────
        [TestMethod]
        public void LoadDirectory_OmittedOptionalFields_FallBackToDefaults()
        {
            WriteFile("minimal.json", @"{ ""exe"": ""minimal.exe"" }");

            CommunityProfileLoadResult result = CommunityProfileLoader.LoadDirectory(_tempDir);

            Assert.AreEqual(1, result.Profiles.Count);
            CommunityProfileEntry entry = result.Profiles[0];
            Assert.AreEqual("minimal.exe", entry.Profile.DisplayName, "DisplayName falls back to exe when 'game' is omitted.");
            Assert.AreEqual("High", entry.Profile.Priority, "Priority defaults to High.");
            Assert.AreEqual(0, entry.Profile.ReservedCores.Length);
            Assert.AreEqual(0, entry.Tweaks.Count);
            Assert.AreEqual("", entry.Notes);
            Assert.IsNull(entry.SubmittedBenchmark);
        }

        // ── Auto-apply hookup: pure match function, no real game / no OS mutation ──────────────
        [TestMethod]
        public void AutoApply_MatchesCommunityProfile_WhenClassifierConfidentlyGaming()
        {
            var profiles = new[]
            {
                new GameProfile { ExeName = "PioneerGame-Win64-Shipping.exe", DisplayName = "Arc Raiders" }
            };
            var decision = new ModeDecision(
                ActivityMode.Gaming, 0.9, "fake watcher event: fullscreen + high GPU",
                new System.Collections.Generic.Dictionary<ActivityMode, double> { [ActivityMode.Gaming] = 0.9 });

            GameProfile? matched = CommunityProfileAutoApply.MatchForAutoApply(
                "PioneerGame-Win64-Shipping.exe", profiles, decision);

            Assert.IsNotNull(matched);
            Assert.AreEqual("Arc Raiders", matched!.DisplayName);
        }

        [TestMethod]
        public void AutoApply_ReturnsNull_WhenClassifierSaysNormal()
        {
            var profiles = new[] { new GameProfile { ExeName = "PioneerGame-Win64-Shipping.exe" } };
            var decision = new ModeDecision(
                ActivityMode.Normal, 0.9, "not a gaming session",
                new System.Collections.Generic.Dictionary<ActivityMode, double> { [ActivityMode.Normal] = 0.9 });

            Assert.IsNull(CommunityProfileAutoApply.MatchForAutoApply(
                "PioneerGame-Win64-Shipping.exe", profiles, decision));
        }

        [TestMethod]
        public void AutoApply_ReturnsNull_WhenConfidenceBelowThreshold()
        {
            var profiles = new[] { new GameProfile { ExeName = "PioneerGame-Win64-Shipping.exe" } };
            var decision = new ModeDecision(
                ActivityMode.Gaming, ConfidenceClassifier.DecisionThreshold - 0.01, "low confidence",
                new System.Collections.Generic.Dictionary<ActivityMode, double>());

            Assert.IsNull(CommunityProfileAutoApply.MatchForAutoApply(
                "PioneerGame-Win64-Shipping.exe", profiles, decision));
        }

        [TestMethod]
        public void AutoApply_ReturnsNull_WhenNoProfileMatchesExe()
        {
            var profiles = new[] { new GameProfile { ExeName = "somethingelse.exe" } };
            var decision = new ModeDecision(
                ActivityMode.Gaming, 0.95, "confident gaming, unrelated exe",
                new System.Collections.Generic.Dictionary<ActivityMode, double> { [ActivityMode.Gaming] = 0.95 });

            Assert.IsNull(CommunityProfileAutoApply.MatchForAutoApply(
                "PioneerGame-Win64-Shipping.exe", profiles, decision));
        }
    }
}
