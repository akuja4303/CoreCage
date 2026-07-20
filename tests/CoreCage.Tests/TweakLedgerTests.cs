using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Ledger;

namespace CoreCage.Tests
{
    /// <summary>
    /// TweakLedger persists "what's active + what it earned you" to a small JSON file
    /// (%LOCALAPPDATA%\CoreCage\ledger.json in production), with an injectable path so every test here
    /// runs against a scratch temp file — never the real one. Mirrors GamingMode's mode-state.json
    /// posture: a missing file is an empty ledger, a corrupted file is an empty ledger, neither throws.
    /// </summary>
    [TestClass]
    public class TweakLedgerTests
    {
        private string _path = "";

        [TestInitialize]
        public void Setup() =>
            _path = Path.Combine(Path.GetTempPath(), $"corecage-ledger-test-{Guid.NewGuid()}.json");

        [TestCleanup]
        public void Cleanup()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch { /* best-effort */ }
        }

        [TestMethod]
        public void Record_RoundTrips_ThroughTempFilePath()
        {
            var ledger = new TweakLedger(_path);
            var entry = new LedgerEntry("gaming-pipeline", DateTimeOffset.UtcNow, true, null, null, null, null);

            ledger.Record(entry);
            ledger.Save();

            var reloaded = TweakLedger.Load(_path);
            Assert.AreEqual(1, reloaded.Entries.Count);
            Assert.AreEqual("gaming-pipeline", reloaded.Entries[0].TweakId);
            Assert.IsTrue(reloaded.Entries[0].Active);
            Assert.IsNull(reloaded.Entries[0].BaselineFps);
        }

        [TestMethod]
        public void Record_RoundTrips_BenchmarkNumbers()
        {
            var ledger = new TweakLedger(_path);
            var entry = new LedgerEntry("eac-polish", DateTimeOffset.UtcNow, true, 130.0, 88.5, 142.3, 96.1);

            ledger.Record(entry);
            ledger.Save();

            var reloaded = TweakLedger.Load(_path);
            var e = reloaded.Entries.Single(x => x.TweakId == "eac-polish");
            Assert.AreEqual(130.0, e.BaselineFps);
            Assert.AreEqual(88.5, e.BaselineOnePctLow);
            Assert.AreEqual(142.3, e.AfterFps);
            Assert.AreEqual(96.1, e.AfterOnePctLow);
        }

        [TestMethod]
        public void Deactivate_FlipsActive_ToFalse()
        {
            var ledger = new TweakLedger(_path);
            ledger.Record(new LedgerEntry("core-cage", DateTimeOffset.UtcNow, true, null, null, null, null));

            ledger.Deactivate("core-cage");

            Assert.IsFalse(ledger.Entries.Single(e => e.TweakId == "core-cage").Active);
        }

        [TestMethod]
        public void Deactivate_UnknownTweakId_DoesNothing_NeverThrows()
        {
            var ledger = new TweakLedger(_path);
            ledger.Record(new LedgerEntry("core-cage", DateTimeOffset.UtcNow, true, null, null, null, null));

            Exception? thrown = null;
            try { ledger.Deactivate("no-such-tweak"); }
            catch (Exception ex) { thrown = ex; }

            Assert.IsNull(thrown);
            Assert.AreEqual(1, ledger.Entries.Count);
        }

        [TestMethod]
        public void Record_SameTweakId_ReplacesThePreviousEntry_NotAppends()
        {
            // TweakLedger tracks "current state per tweak", not a full history log — Prove It re-Records
            // the same TweakId with benchmark numbers filled in, and that must replace the un-benchmarked
            // row rather than leaving a duplicate for the UI to disambiguate.
            var ledger = new TweakLedger(_path);
            ledger.Record(new LedgerEntry("gaming-pipeline", DateTimeOffset.UtcNow, true, null, null, null, null));
            ledger.Record(new LedgerEntry("gaming-pipeline", DateTimeOffset.UtcNow, true, 130.0, 88.0, 142.0, 95.0));

            Assert.AreEqual(1, ledger.Entries.Count);
            Assert.AreEqual(142.0, ledger.Entries[0].AfterFps);
        }

        [TestMethod]
        public void Load_MissingFile_ReturnsEmptyLedger_NeverThrows()
        {
            Assert.IsFalse(File.Exists(_path));

            var ledger = TweakLedger.Load(_path);

            Assert.AreEqual(0, ledger.Entries.Count);
        }

        [TestMethod]
        public void Load_CorruptedFile_ReturnsEmptyLedger_NeverThrows()
        {
            File.WriteAllText(_path, "{ this is not valid json ]]]");

            TweakLedger? ledger = null;
            Exception? thrown = null;
            try { ledger = TweakLedger.Load(_path); }
            catch (Exception ex) { thrown = ex; }

            Assert.IsNull(thrown, "a corrupted ledger file must never throw out of Load.");
            Assert.IsNotNull(ledger);
            Assert.AreEqual(0, ledger!.Entries.Count);
        }

        [TestMethod]
        public void Save_IsAtomic_ProducesLoadableFile_AndLeavesNoTmpBehind()
        {
            // A direct File.WriteAllText that crashes mid-write corrupts the ledger and Load silently
            // discards all proof. Save must write to a .tmp sibling then File.Move it into place so a
            // reader only ever sees either the old complete file or the new complete file, never a
            // half-written one.
            var ledger = new TweakLedger(_path);
            ledger.Record(new LedgerEntry("gaming-stack", DateTimeOffset.UtcNow, true, 130.0, 88.0, 142.3, 96.1));

            ledger.Save();

            Assert.IsTrue(File.Exists(_path), "Save must produce the ledger file.");
            Assert.IsFalse(File.Exists(_path + ".tmp"), "Save must not leave a .tmp file behind.");

            var reloaded = TweakLedger.Load(_path);
            Assert.AreEqual(1, reloaded.Entries.Count);
            Assert.AreEqual("gaming-stack", reloaded.Entries[0].TweakId);
            Assert.AreEqual(142.3, reloaded.Entries[0].AfterFps);
        }
    }
}
