using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Telemetry;

namespace CoreCage.Tests
{
    [TestClass]
    public class PresentMonCsvTests
    {
        // A header matching the real PresentMon schema (subset), frametime not in the first column.
        private static readonly string[] Sample =
        {
            "Application,ProcessID,MsBetweenPresents,MsBetweenDisplayChange,MsRenderPresentLatency",
            "PioneerGame-d.exe,12136,8.6588,6.8688,0.9959",
            "PioneerGame-d.exe,12136,7.0816,8.4438,2.3581",
            "PioneerGame-d.exe,12136,NA,9.0000,1.0000",   // NA frametime row → skipped
            "PioneerGame-d.exe,12136,9.2500,7.1000,1.5000",
        };

        [TestMethod]
        public void ParseColumn_ExtractsFrameTime_ByName_SkipsNA()
        {
            var ft = PresentMonCsv.ParseColumn(Sample); // default column = MsBetweenPresents

            CollectionAssert.AreEqual(new List<double> { 8.6588, 7.0816, 9.2500 }, ft);
        }

        [TestMethod]
        public void ParseColumn_RespectsColumnOrder_ForDifferentColumn()
        {
            var disp = PresentMonCsv.ParseColumn(Sample, PresentMonCsv.DisplayedColumn);

            CollectionAssert.AreEqual(new List<double> { 6.8688, 8.4438, 9.0000, 7.1000 }, disp);
        }

        [TestMethod]
        public void ParseColumn_MissingColumn_ReturnsEmpty()
        {
            var ft = PresentMonCsv.ParseColumn(Sample, "NoSuchColumn");
            Assert.AreEqual(0, ft.Count);
        }

        [TestMethod]
        public void ParseColumn_HeaderOnly_ReturnsEmpty()
        {
            var ft = PresentMonCsv.ParseColumn(new[] { "Application,ProcessID,MsBetweenPresents" });
            Assert.AreEqual(0, ft.Count);
        }

        [TestMethod]
        public void ParseColumn_SkipsBlankLinesAndShortRows()
        {
            var lines = new[]
            {
                "Application,MsBetweenPresents",
                "",
                "OnlyOneField",            // shorter than the column index → skipped
                "game.exe,5.0",
                "   ",
                "game.exe,6.0",
            };

            var ft = PresentMonCsv.ParseColumn(lines);

            CollectionAssert.AreEqual(new List<double> { 5.0, 6.0 }, ft);
        }

        [TestMethod]
        public void ParseColumn_EndToEnd_FeedsFrametimeStats()
        {
            var ft = PresentMonCsv.ParseColumn(Sample);
            var stats = FrametimeStats.FromFrametimes(ft);

            Assert.AreEqual(3, stats.FrameCount);
            Assert.IsTrue(stats.AvgFps > 0);
        }
    }
}
