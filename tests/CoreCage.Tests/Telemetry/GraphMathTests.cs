using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Telemetry;

namespace CoreCage.Tests.Telemetry
{
    [TestClass]
    public class GraphMathTests
    {
        [TestMethod]
        public void FewerThanTwoSamples_ReturnsEmpty()
        {
            Assert.AreEqual(0, GraphMath.BuildPoints(new double[0], 100, 50, 0, 100).Length);
            Assert.AreEqual(0, GraphMath.BuildPoints(new double[] { 42 }, 100, 50, 0, 100).Length);
        }

        [TestMethod]
        public void TwoSamples_MapCornersWithInvertedY()
        {
            var p = GraphMath.BuildPoints(new double[] { 0, 100 }, 100, 50, 0, 100);
            Assert.AreEqual(2, p.Length);
            Assert.AreEqual(0.0,   p[0].X, 1e-9);
            Assert.AreEqual(50.0,  p[0].Y, 1e-9);   // value 0 (min) -> bottom (y=height)
            Assert.AreEqual(100.0, p[1].X, 1e-9);
            Assert.AreEqual(0.0,   p[1].Y, 1e-9);   // value 100 (max) -> top (y=0)
        }

        [TestMethod]
        public void EvenlySpacedOnX()
        {
            var p = GraphMath.BuildPoints(new double[] { 10, 20, 30 }, 100, 50, 0, 100);
            Assert.AreEqual(0.0,  p[0].X, 1e-9);
            Assert.AreEqual(50.0, p[1].X, 1e-9);
            Assert.AreEqual(100.0,p[2].X, 1e-9);
        }

        [TestMethod]
        public void ValuesAreClampedToRange()
        {
            var p = GraphMath.BuildPoints(new double[] { -10, 150 }, 100, 50, 0, 100);
            Assert.AreEqual(50.0, p[0].Y, 1e-9);   // below min -> bottom
            Assert.AreEqual(0.0,  p[1].Y, 1e-9);   // above max -> top
        }

        [TestMethod]
        public void FlatWhenMaxNotGreaterThanMin()
        {
            var p = GraphMath.BuildPoints(new double[] { 5, 9, 2 }, 100, 50, 100, 100);
            foreach (var pt in p) Assert.AreEqual(25.0, pt.Y, 1e-9);   // height/2
        }
    }
}
