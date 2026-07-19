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
}
