using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Telemetry;

namespace CoreCage.Tests.Telemetry
{
    [TestClass]
    public class FrameGenAnalysisTests
    {
        private static List<double> Rep(double ms, int n) => Enumerable.Repeat(ms, n).ToList();

        [TestMethod]
        public void FrameGen_On_DisplayedDoublesPresented_IsFlagged()
        {
            // 60 fps rendered (16.667 ms), 120 fps displayed (8.333 ms) -> FG doubling.
            var fg = FrameGenAnalysis.From(Rep(16.667, 120), Rep(8.333, 120));
            Assert.IsTrue(fg.FrameGenLikely);
            Assert.AreEqual(60.0, fg.PresentedFps, 0.5);
            Assert.AreEqual(120.0, fg.DisplayedFps, 0.5);
            Assert.IsTrue(fg.Ratio > 1.9 && fg.Ratio < 2.1);
            Assert.AreEqual(50.0, fg.GeneratedPct, 1.0);
        }

        [TestMethod]
        public void FrameGen_Off_SimilarCadence_NotFlagged()
        {
            var fg = FrameGenAnalysis.From(Rep(16.667, 120), Rep(16.7, 120));
            Assert.IsFalse(fg.FrameGenLikely);
            Assert.AreEqual(0.0, fg.GeneratedPct, 1e-9);
            Assert.IsTrue(fg.PresentedFps > 0);
        }

        [TestMethod]
        public void MissingDisplayedColumn_NotFlagged_StillReportsPresented()
        {
            var fg = FrameGenAnalysis.From(Rep(10.0, 120), new List<double>());
            Assert.IsFalse(fg.FrameGenLikely);
            Assert.AreEqual(100.0, fg.PresentedFps, 0.5);
            Assert.AreEqual(0.0, fg.DisplayedFps, 1e-9);
        }

        [TestMethod]
        public void TooFewFrames_NotFlagged_EvenWhenCadenceGapIsLarge()
        {
            var fg = FrameGenAnalysis.From(Rep(16.667, 10), Rep(8.333, 10));
            Assert.IsFalse(fg.FrameGenLikely);
        }
    }
}
