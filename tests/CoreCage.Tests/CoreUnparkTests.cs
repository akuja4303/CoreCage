using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;

namespace CoreCage.Tests
{
    /// <summary>
    /// Pure-logic tests for the council-picked CoreUnpark feature: the powercfg argument builder and the
    /// `powercfg /query` output parser. The live powercfg calls can't be unit-tested (they mutate the
    /// active power plan), but the args we emit and the originals we capture must be exactly right.
    /// </summary>
    [TestClass]
    public class CoreUnparkTests
    {
        [TestMethod]
        public void BuildApplyArgs_SetsAllThreeSettingsTo100_OnBothAcAndDc()
        {
            var args = CoreUnpark.BuildApplyArgs();

            // 3 settings × (AC + DC) = 6 commands.
            Assert.AreEqual(6, args.Count);

            // CPMINCORES (unpark), CPMAXCORES, PROCTHROTTLEMIN (perf floor) — each on AC and DC, all value 100.
            const string cpMin  = "0cc5b647-c1df-4637-891a-dec35c318583";
            const string cpMax  = "ea062031-0e34-4ff1-9b6d-eb1059334028";
            const string thrMin = "893dee8e-2bef-41e0-89c6-b55d0929964c";

            foreach (var setting in new[] { cpMin, cpMax, thrMin })
            {
                Assert.IsTrue(args.Any(a => a.Contains("/setacvalueindex") && a.Contains(setting) && a.EndsWith(" 100")),
                    $"missing AC apply for {setting}");
                Assert.IsTrue(args.Any(a => a.Contains("/setdcvalueindex") && a.Contains(setting) && a.EndsWith(" 100")),
                    $"missing DC apply for {setting}");
            }
        }

        [TestMethod]
        public void BuildApplyArgs_TargetsTheActiveScheme_AndNeverTouchesIdleDisable()
        {
            var args = CoreUnpark.BuildApplyArgs();
            Assert.IsTrue(args.All(a => a.Contains("scheme_current")), "all writes must target the active scheme");
            // IDLEDISABLE / C-state knobs are the audio-crackle hazard the council told us NOT to touch.
            Assert.IsFalse(args.Any(a => a.ToUpperInvariant().Contains("IDLEDISABLE")),
                "CoreUnpark must NOT touch processor idle-disable");
        }

        [TestMethod]
        public void ParseAcIndex_ReadsTheCurrentAcIndexFollowingTheSettingGuid()
        {
            // Trimmed shape of real `powercfg /query scheme_current SUB_PROCESSOR` output.
            const string query = @"
    Subgroup GUID: 54533251-82be-4824-96c1-47b60b740d00  (Processor power management)
      Power Setting GUID: 0cc5b647-c1df-4637-891a-dec35c318583  (Processor core parking min cores)
        Current AC Power Setting Index: 0x00000005
        Current DC Power Setting Index: 0x00000005
      Power Setting GUID: 893dee8e-2bef-41e0-89c6-b55d0929964c  (Minimum processor state)
        Current AC Power Setting Index: 0x00000064
        Current DC Power Setting Index: 0x00000005";

            Assert.AreEqual(5,   CoreUnpark.ParseAcIndex(query, "0cc5b647-c1df-4637-891a-dec35c318583"), "CPMINCORES AC = 0x5");
            Assert.AreEqual(100, CoreUnpark.ParseAcIndex(query, "893dee8e-2bef-41e0-89c6-b55d0929964c"), "PROCTHROTTLEMIN AC = 0x64 = 100");
        }

        [TestMethod]
        public void ParseAcIndex_ReturnsNull_WhenSettingAbsentOrInputEmpty()
        {
            const string query = "Power Setting GUID: 0cc5b647-c1df-4637-891a-dec35c318583\n        Current AC Power Setting Index: 0x00000005";
            Assert.IsNull(CoreUnpark.ParseAcIndex(query, "ea062031-0e34-4ff1-9b6d-eb1059334028"), "absent GUID → null");
            Assert.IsNull(CoreUnpark.ParseAcIndex("", "0cc5b647-c1df-4637-891a-dec35c318583"), "empty input → null");
        }
    }
}
