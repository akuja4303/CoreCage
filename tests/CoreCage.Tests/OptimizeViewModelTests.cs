using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.App;

namespace CoreCage.Tests;

/// <summary>
/// The Optimize group runs the real mode actions (Gaming / Restore, routed through
/// CoreCage.Core.Modes.ModeRegistry.Get("Gaming")). These prove it calls through to the service,
/// reports honest success/failure, disables its buttons while an action is in flight, and never
/// crashes when an action throws. Uses a fake so no test ever tweaks the machine. CoreCage.App is
/// gaming-only — there is no other performance-mode surface to test here.
/// </summary>
[TestClass]
public sealed class OptimizeViewModelTests
{
    [TestMethod]
    public async Task Gaming_applies_and_reports_success()
    {
        var fake = new FakeOptimizeService();
        var vm = new OptimizeViewModel(fake);

        await vm.ApplyGamingAsync();

        Assert.AreEqual(1, fake.GamingCalls);
        Assert.IsTrue(vm.LastOk);
        StringAssert.Contains(vm.StatusMessage, "Gaming");
        Assert.IsFalse(vm.IsBusy, "busy must clear when the action finishes");
        Assert.IsTrue(vm.GamingIsActive, "GamingIsActive should refresh from the service after apply");
    }

    [TestMethod]
    public async Task Restore_calls_the_mode_module_revert()
    {
        var fake = new FakeOptimizeService { RestoreResult = new(true, "Restored 47 changes.") };
        var vm = new OptimizeViewModel(fake);

        await vm.RestoreAsync();

        Assert.AreEqual(1, fake.RestoreCalls);
        Assert.IsTrue(vm.LastOk);
        StringAssert.Contains(vm.StatusMessage, "47");
    }

    [TestMethod]
    public async Task Failed_action_surfaces_error_without_crashing()
    {
        var fake = new FakeOptimizeService { ThrowOnGaming = true };
        var vm = new OptimizeViewModel(fake);

        await vm.ApplyGamingAsync();

        Assert.IsFalse(vm.LastOk, "a thrown action reports failure, not success");
        Assert.IsFalse(vm.IsBusy, "busy clears even on failure");
        StringAssert.Contains(vm.StatusMessage, "failed");
    }

    [TestMethod]
    public async Task Buttons_are_disabled_while_an_action_is_in_flight()
    {
        var fake = new FakeOptimizeService();
        fake.Release.Reset(); // hold ApplyGamingAsync open
        var vm = new OptimizeViewModel(fake);

        var running = vm.ApplyGamingAsync();
        Assert.IsTrue(fake.Entered.Wait(2000), "the action should have started");

        Assert.IsTrue(vm.IsBusy);
        Assert.IsFalse(vm.GamingCommand.CanExecute(null), "buttons disabled while busy");
        Assert.IsFalse(vm.RestoreCommand.CanExecute(null));

        fake.Release.Set();
        await running;

        Assert.IsTrue(vm.GamingCommand.CanExecute(null), "buttons re-enable when done");
    }

    [TestMethod]
    public void CoreCage_settings_read_their_initial_values_from_the_service()
    {
        var fake = new FakeOptimizeService { CoreCageEnabledValue = false, CoreCageReservedCoresValue = 3, LogicalCoreCount = 8 };
        var vm = new OptimizeViewModel(fake);

        Assert.IsFalse(vm.CoreCageEnabled);
        Assert.AreEqual(3, vm.CoreCageReservedCores);
        Assert.AreEqual(8, vm.LogicalCoreCount);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7 }, vm.AvailableReservedCoreCounts.ToList(),
            "picker must stop at LogicalCoreCount-1 so at least one core is always left for the cage.");
    }

    [TestMethod]
    public void CoreCageEnabled_change_persists_through_the_service()
    {
        var fake = new FakeOptimizeService { CoreCageEnabledValue = true };
        var vm = new OptimizeViewModel(fake);

        vm.CoreCageEnabled = false;

        Assert.AreEqual(1, fake.CoreCageEnabledWrites);
        Assert.IsFalse(fake.CoreCageEnabledValue);
    }

    [TestMethod]
    public void CoreCageReservedCores_change_persists_through_the_service()
    {
        var fake = new FakeOptimizeService { LogicalCoreCount = 12, CoreCageReservedCoresValue = 6 };
        var vm = new OptimizeViewModel(fake);

        vm.CoreCageReservedCores = 8;

        Assert.AreEqual(8, vm.CoreCageReservedCores);
        Assert.AreEqual(1, fake.CoreCageReservedCoresWrites);
        Assert.AreEqual(8, fake.CoreCageReservedCoresValue);
    }

    [TestMethod]
    public void CoreCageReservedCores_clamps_so_at_least_one_core_is_left_for_the_cage()
    {
        var fake = new FakeOptimizeService { LogicalCoreCount = 8, CoreCageReservedCoresValue = 4 };
        var vm = new OptimizeViewModel(fake);

        vm.CoreCageReservedCores = 20; // way over the machine's core count

        Assert.AreEqual(7, vm.CoreCageReservedCores, "must clamp to LogicalCoreCount-1, never consume every core.");
        Assert.AreEqual(7, fake.CoreCageReservedCoresValue, "the clamped value, not the raw input, must be persisted.");
    }

    [TestMethod]
    public async Task CoreCage_settings_disable_while_an_action_is_in_flight()
    {
        var fake = new FakeOptimizeService();
        fake.Release.Reset();
        var vm = new OptimizeViewModel(fake);

        var running = vm.ApplyGamingAsync();
        Assert.IsTrue(fake.Entered.Wait(2000), "the action should have started");

        Assert.IsFalse(vm.CanEditCoreCageSettings, "Core Cage settings disable while busy, same as the buttons");

        fake.Release.Set();
        await running;

        Assert.IsTrue(vm.CanEditCoreCageSettings, "Core Cage settings re-enable when done");
    }
}
