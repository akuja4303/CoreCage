using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Latency;

namespace CoreCage.Tests
{
    [TestClass]
    public class TimerResolutionPolicyTests
    {
        [TestMethod]
        public void HundredNs_Conversions_Round_Trip()
        {
            Assert.AreEqual(5000L, TimerResolutionPolicy.ToHundredNs(0.5));
            Assert.AreEqual(10000L, TimerResolutionPolicy.ToHundredNs(1.0));
            Assert.AreEqual(0.5, TimerResolutionPolicy.FromHundredNs(5000), 1e-9);
            Assert.AreEqual(1.0, TimerResolutionPolicy.FromHundredNs(10000), 1e-9);
        }

        [TestMethod]
        public void Candidates_Within_Range_Sorted_Distinct()
        {
            CollectionAssert.AreEqual(new[] { 0.5, 1.0 },
                (System.Collections.ICollection)TimerResolutionPolicy.Candidates(0.5, 1.0));

            var withFinest = TimerResolutionPolicy.Candidates(0.4, 1.0);
            CollectionAssert.AreEqual(new[] { 0.4, 0.5, 1.0 }, (System.Collections.ICollection)withFinest);

            // coarsest below 1.0 drops the 1.0 candidate
            CollectionAssert.AreEqual(new[] { 0.5 },
                (System.Collections.ICollection)TimerResolutionPolicy.Candidates(0.5, 0.6));
        }

        [TestMethod]
        public void PickBest_Chooses_Lowest_Overshoot()
        {
            double best = TimerResolutionPolicy.PickBest(new (double, double)[]
            {
                (0.5, 1.20),
                (1.0, 0.95),
            });
            Assert.AreEqual(1.0, best); // 1.0 ms had less jitter than 0.5 ms here
        }

        [TestMethod]
        public void PickBest_Tie_Prefers_Finer_Regardless_Of_Order()
        {
            Assert.AreEqual(0.5, TimerResolutionPolicy.PickBest(new (double, double)[] { (0.5, 1.0), (1.0, 1.0) }));
            Assert.AreEqual(0.5, TimerResolutionPolicy.PickBest(new (double, double)[] { (1.0, 1.0), (0.5, 1.0) }));
        }

        [TestMethod]
        public void PickBest_Empty_Returns_Zero()
        {
            Assert.AreEqual(0.0, TimerResolutionPolicy.PickBest(new List<(double, double)>()));
        }
    }
}
