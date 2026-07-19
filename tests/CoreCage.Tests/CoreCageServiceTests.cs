using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Caging;

namespace CoreCage.Tests
{
    /// <summary>
    /// TDD coverage for the pure planner half of Core Cage (the flagship feature: reserve top cores
    /// for the game, cage everything else onto the leftover cores — the technique measured 77→~150fps
    /// in Arc Raiders). <see cref="CoreCageService.BuildPlan"/> takes NO Process/OS dependency at all,
    /// so every case here is a real, deterministic assert on the returned masks/pids.
    ///
    /// <see cref="CoreCageService.Apply"/>/<see cref="CoreCageService.Release"/> are intentionally NOT
    /// invoked anywhere in this file — they call <c>Process.GetProcessById(pid).ProcessorAffinity</c>
    /// and would mutate real processes on whatever machine runs the suite. Their real behavior is
    /// verified live in Task 11, same pattern as GamingMode's pipeline.
    /// </summary>
    [TestClass]
    public class CoreCageServiceTests
    {
        [TestMethod]
        public void BuildPlan_12Cores_8Reserved_TopCoresForGame_RemainingCoresForCage()
        {
            var plan = CoreCageService.BuildPlan(
                totalCores: 12,
                reservedForGame: 8,
                processes: Array.Empty<(int Pid, string Name)>(),
                whitelist: new HashSet<string>());

            Assert.AreEqual(0b111111110000L, plan.GameMask, "top 8 of 12 cores (bits 4-11) reserved for the game.");
            Assert.AreEqual(0b000000001111L, plan.CagedMask, "remaining bottom 4 cores (bits 0-3) are the cage.");
        }

        [TestMethod]
        public void BuildPlan_ExcludesWhitelistedProcesses_GameExe_Audiodg_AndWhitelistEntries()
        {
            var processes = new List<(int Pid, string Name)>
            {
                (100, "MyGame.exe"),   // the game itself — must never be caged
                (200, "audiodg"),      // Windows audio engine — always protected
                (300, "chrome"),       // ordinary background process — should be caged
                (400, "SomeTool.exe"), // user's own whitelist entry — must never be caged
            };
            var whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mygame", "audiodg", "sometool" };

            var plan = CoreCageService.BuildPlan(
                totalCores: 8,
                reservedForGame: 4,
                processes: processes,
                whitelist: whitelist);

            CollectionAssert.DoesNotContain(new List<int>(plan.CagedPids), 100, "game exe must never be caged.");
            CollectionAssert.DoesNotContain(new List<int>(plan.CagedPids), 200, "audiodg must never be caged.");
            CollectionAssert.DoesNotContain(new List<int>(plan.CagedPids), 400, "explicit whitelist entries must never be caged.");
            CollectionAssert.Contains(new List<int>(plan.CagedPids), 300, "an ordinary background process should be caged.");
            Assert.AreEqual(1, plan.CagedPids.Count, "only the one non-whitelisted process should be caged.");
        }

        [TestMethod]
        public void BuildPlan_ReservedForGame_EqualToTotalCores_Throws()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                CoreCageService.BuildPlan(8, 8, Array.Empty<(int Pid, string Name)>(), new HashSet<string>()));
        }

        [TestMethod]
        public void BuildPlan_ReservedForGame_GreaterThanTotalCores_Throws()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                CoreCageService.BuildPlan(8, 12, Array.Empty<(int Pid, string Name)>(), new HashSet<string>()));
        }

        [TestMethod]
        public void BuildPlan_TwoCoreMachine_RefusesCaging_EmptyCagedPids()
        {
            var processes = new List<(int Pid, string Name)>
            {
                (500, "chrome"),
                (600, "steam"),
            };

            var plan = CoreCageService.BuildPlan(
                totalCores: 2,
                reservedForGame: 1,
                processes: processes,
                whitelist: new HashSet<string>());

            Assert.AreEqual(0, plan.CagedPids.Count, "a 2-core machine has nowhere meaningful to cage to — refuse.");
        }

        [TestMethod]
        public void BuildPlan_NullProcesses_ReturnsEmptyCagedPids_WithoutThrowing()
        {
            var plan = CoreCageService.BuildPlan(8, 4, null!, new HashSet<string>());
            Assert.AreEqual(0, plan.CagedPids.Count);
        }
    }
}
