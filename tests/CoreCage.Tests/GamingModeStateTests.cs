using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Modes;

namespace CoreCage.Tests
{
    /// <summary>
    /// Task-3 review follow-up: GamingMode's <c>IsActive</c> persistence was structurally testable
    /// (constructor-injectable <c>statePath</c>) but never actually exercised — Task 3's own tests never
    /// invoke <c>ApplyAsync</c>/<c>RevertAsync</c> since those mutate the rig. These tests drive the same
    /// state file GamingMode reads/writes directly (pure file I/O to a scratch temp path — no OS
    /// mutation, no engine pipeline invoked), proving the round-trip and the corrupted-file fallback that
    /// <c>LoadState</c>'s try/catch is supposed to provide.
    /// </summary>
    [TestClass]
    public class GamingModeStateTests
    {
        private string _statePath = "";

        [TestInitialize]
        public void Setup() =>
            _statePath = Path.Combine(Path.GetTempPath(), $"corecage-mode-state-test-{Guid.NewGuid()}.json");

        [TestCleanup]
        public void Cleanup()
        {
            try { if (File.Exists(_statePath)) File.Delete(_statePath); } catch { /* best-effort cleanup */ }
        }

        [TestMethod]
        public void IsActive_RoundTrips_ThroughTheStateFile_AcrossInstances()
        {
            var first = new GamingMode(_statePath);
            Assert.IsFalse(first.IsActive, "no state file yet -> defaults to inactive.");

            // True round-trip: drive GamingMode's OWN internal save path (SaveState) instead of
            // hand-writing the JSON, without ever invoking ApplyAsync (which would touch the real rig).
            first.SaveState(true);

            Assert.IsTrue(first.IsActive, "the same instance re-reads the file on every IsActive get.");

            var second = new GamingMode(_statePath);
            Assert.IsTrue(second.IsActive, "a brand-new instance pointed at the same path sees the persisted state too.");
        }

        [TestMethod]
        public void IsActive_CorruptedStateFile_ReadsAsFalse_NeverThrows()
        {
            File.WriteAllText(_statePath, "{ this is not valid json ]]]");

            var mode = new GamingMode(_statePath);

            bool? isActive = null;
            Exception? thrown = null;
            try { isActive = mode.IsActive; }
            catch (Exception ex) { thrown = ex; }

            Assert.IsNull(thrown, "a corrupted state file must never throw out of IsActive.");
            Assert.IsFalse(isActive, "corrupted state is treated as inactive (safe default), not a crash.");
        }
    }
}
