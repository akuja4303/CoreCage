using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;
using CoreCage.Core.Caging;
using CoreCage.Core.Modes;

namespace CoreCage.Tests
{
    /// <summary>
    /// Review finding IMPORTANT-1: GamingMode.RevertAsync gated Core Cage release on
    /// FeatureFlags.Current.CoreCageEnabled, so flipping the toggle off after Apply left the game
    /// (and everything else) permanently pinned to the caged mask -- Release itself is idempotent and
    /// always-safe, so gating it on the CURRENT flag value (rather than "did we actually cage
    /// something") was the bug. These tests drive RevertAsync through a fully faked pipeline (an
    /// internal test constructor swaps every delegate for an in-memory fake) so no OS mutation ever
    /// happens, and assert on the release decision itself.
    /// </summary>
    [TestClass]
    public class GamingModeRevertReleaseTests
    {
        private string _statePath = "";
        private bool _originalCoreCageEnabled;

        [TestInitialize]
        public void Setup()
        {
            _statePath = Path.Combine(Path.GetTempPath(), $"corecage-revert-release-test-{Guid.NewGuid()}.json");
            _originalCoreCageEnabled = FeatureFlags.Current.CoreCageEnabled;
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { if (File.Exists(_statePath)) File.Delete(_statePath); } catch { /* best-effort */ }
            FeatureFlags.Current.CoreCageEnabled = _originalCoreCageEnabled;
        }

        private static GamingMode NewFakedMode(string statePath, Func<int> releaseCoreCage) =>
            new GamingMode(
                statePath,
                releaseCoreCage: releaseCoreCage,
                restorePolish: _ => 0,
                restoreGamingModePlusPlus: () => { },
                restoreCoreUnpark: () => false,
                gamingProcessList: () => Array.Empty<string>(),
                restoreEverything: () => new RestoreSummary());

        [TestMethod]
        public async Task RevertAsync_ReleasesCage_WhenLastPlanExists_EvenIfFlagIsOff()
        {
            FeatureFlags.Current.CoreCageEnabled = false;

            bool releaseCalled = false;
            var mode = NewFakedMode(_statePath, () => { releaseCalled = true; return 2; });
            mode.LastCagePlanForTests = new CagePlan(0, 0, new List<int> { 111, 222 });

            ModeResult result = await mode.RevertAsync();

            Assert.IsTrue(releaseCalled,
                "Release must run whenever a cage plan exists, regardless of the CURRENT flag value -- " +
                "Release is idempotent/always-safe and must undo whatever Apply actually did.");
            Assert.IsTrue(result.Success);
            CollectionAssert.Contains(new List<string>(result.Steps), "Core Cage released (2 process(es))");
        }

        [TestMethod]
        public async Task RevertAsync_SkipsRelease_WhenNoLastPlanExists()
        {
            FeatureFlags.Current.CoreCageEnabled = true;

            bool releaseCalled = false;
            var mode = NewFakedMode(_statePath, () => { releaseCalled = true; return 2; });
            // _lastCagePlan stays null (default) -- nothing was ever caged, so nothing to release.

            ModeResult result = await mode.RevertAsync();

            Assert.IsFalse(releaseCalled, "Release must not run when there's no plan to release.");
            Assert.IsTrue(result.Success);
        }
    }
}
