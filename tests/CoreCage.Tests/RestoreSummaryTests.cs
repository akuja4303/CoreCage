using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;

namespace CoreCage.Tests
{
    /// <summary>
    /// Tests the pure reporting logic of RestoreSummary. The RestoreEverything system calls
    /// can't be unit-tested (they touch the live registry/services), but the summary math —
    /// which is what the user actually sees ("N changes reversed") — is pure and must be honest.
    /// </summary>
    [TestClass]
    public class RestoreSummaryTests
    {
        [TestMethod]
        public void EmptySummary_ReportsZeroChanges()
        {
            var s = new RestoreSummary();
            StringAssert.Contains(s.ForUser(), "0 change(s) reversed");
        }

        [TestMethod]
        public void ForUser_CountsBooleanWeightsAndIntFields_IncludingReenabledServices()
        {
            var s = new RestoreSummary
            {
                GamingPlusReverted    = true,   // weight 5
                IfeoEntriesCleared    = 2,      // +2
                ServicesResumed       = true,   // weight 3
                ServicesReenabled     = 3,      // +3  (the newly-wired telemetry/search/spooler restore)
                TdrDelayReset         = true,   // +1
                PowerPlanReset        = true,   // +1
                NetworkReset          = true,   // weight 5
                ProcessPrioritiesReset= 4,      // +4
                TimerResolutionReset  = true,   // +1
                AutoStartTasksRemoved = true,   // weight 3
                RegistrySnapshotsRestored = 1,  // +1
            };
            // 5 + 2 + 3 + 3 + 1 + 1 + 5 + 4 + 1 + 3 + 1 = 29
            StringAssert.Contains(s.ForUser(), "29 change(s) reversed");
        }

        [TestMethod]
        public void FailedOperations_AreNotCounted()
        {
            // Every operation reported failure → the user must NOT be told changes were made.
            var s = new RestoreSummary
            {
                PowerPlanReset       = false,
                NetworkReset         = false,
                TimerResolutionReset = false,
                TdrDelayReset        = false,
                ServicesReenabled    = 0,
            };
            StringAssert.Contains(s.ForUser(), "0 change(s) reversed");
        }

        [TestMethod]
        public void ToString_SurfacesReenabledServicesCount()
        {
            var s = new RestoreSummary { ServicesReenabled = 3 };
            StringAssert.Contains(s.ToString(), "services re-enabled=3");
        }
    }
}
