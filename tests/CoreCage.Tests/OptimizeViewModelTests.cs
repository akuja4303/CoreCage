using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.App;
using CoreCage.App.Services;

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

    // ------------------------------------------------------------------
    // Tweak Ledger card + "Prove it" (Task 6)
    // ------------------------------------------------------------------

    [TestMethod]
    public void No_ledger_rows_shows_the_empty_state()
    {
        var fake = new FakeOptimizeService(); // LedgerRowsValue defaults to empty
        var vm = new OptimizeViewModel(fake);

        Assert.IsTrue(vm.IsLedgerEmpty);
        Assert.AreEqual(0, vm.LedgerRows.Count);
    }

    [TestMethod]
    public void WholeStack_row_shows_measured_delta_when_benchmarked()
    {
        // CRITICAL-2 fix: the measured A/B lands on a single "gaming-stack" row, not copied onto every
        // active per-tweak row -- a single whole-stack bench can't attribute causation per-tweak.
        var fake = new FakeOptimizeService
        {
            LedgerRowsValue = { new LedgerRowInfo("gaming-stack", true, 130.0, 88.0, 142.3, 96.1) }
        };
        var vm = new OptimizeViewModel(fake);

        Assert.IsFalse(vm.IsLedgerEmpty);
        var row = vm.LedgerRows.Single();
        Assert.AreEqual("Gaming Mode (whole stack)", row.DisplayName);
        Assert.IsTrue(row.Active);
        StringAssert.Contains(row.DeltaText, "FPS +12.3");
        StringAssert.Contains(row.DeltaText, "1% lows +8.1");
    }

    [TestMethod]
    public void Step_row_shows_measured_as_part_of_whole_stack_once_the_whole_stack_row_is_proven()
    {
        var fake = new FakeOptimizeService
        {
            LedgerRowsValue =
            {
                new LedgerRowInfo("gaming-stack", true, 130.0, 88.0, 142.3, 96.1),
                new LedgerRowInfo("core-cage", true, null, null, null, null),
            }
        };
        var vm = new OptimizeViewModel(fake);

        var stepRow = vm.LedgerRows.Single(r => r.DisplayName == "Core Cage");
        Assert.IsTrue(stepRow.Active, "step row keeps its Active status.");
        Assert.AreEqual("measured as part of whole stack", stepRow.DeltaText,
            "must never show a copied per-tweak delta -- a single A/B can't attribute causation.");
    }

    [TestMethod]
    public void Ledger_rows_show_not_yet_benchmarked_when_never_proven()
    {
        var fake = new FakeOptimizeService
        {
            LedgerRowsValue = { new LedgerRowInfo("core-cage", true, null, null, null, null) }
        };
        var vm = new OptimizeViewModel(fake);

        Assert.AreEqual("not yet benchmarked", vm.LedgerRows.Single().DeltaText);
    }

    [TestMethod]
    public void WholeStack_row_with_a_null_onepctlow_field_is_not_treated_as_measured()
    {
        // MINOR-5 fix: LedgerDeltaText must guard ALL FOUR benchmark fields, not just Fps -- a null 1%
        // low must never render as "+0.0" as if it had actually been measured.
        var fake = new FakeOptimizeService
        {
            LedgerRowsValue = { new LedgerRowInfo("gaming-stack", true, 130.0, null, 142.3, 96.1) }
        };
        var vm = new OptimizeViewModel(fake);

        Assert.AreEqual("not yet benchmarked", vm.LedgerRows.Single().DeltaText);
    }

    [TestMethod]
    public async Task ProveIt_runs_and_reports_success_then_refreshes_the_ledger()
    {
        var fake = new FakeOptimizeService
        {
            ProveItResult = new(true, "Proved it: FPS +12.3, 1% lows +8.1."),
        };
        var vm = new OptimizeViewModel(fake);
        // Simulate the service's ledger having been updated by the real ProveItAsync by the time
        // RefreshLedgerRows runs in RunAsync's finally block.
        fake.LedgerRowsValue.Add(new LedgerRowInfo("gaming-pipeline", true, 130.0, 88.0, 142.3, 96.1));

        await vm.ProveItAsync();

        Assert.AreEqual(1, fake.ProveItCalls);
        Assert.IsTrue(vm.LastOk);
        StringAssert.Contains(vm.StatusMessage, "Proved it");
        Assert.IsFalse(vm.IsBusy);
        Assert.IsFalse(vm.IsLedgerEmpty, "ledger should refresh after Prove It completes");
    }

    [TestMethod]
    public async Task ProveIt_missing_PresentMon_surfaces_the_error_without_crashing()
    {
        var fake = new FakeOptimizeService
        {
            ProveItResult = new(false, "PresentMon.exe not found. Download it from https://github.com/GameTechDev/PresentMon/releases and try again."),
        };
        var vm = new OptimizeViewModel(fake);

        await vm.ProveItAsync();

        Assert.IsFalse(vm.LastOk);
        StringAssert.Contains(vm.StatusMessage, "PresentMon");
        Assert.IsFalse(vm.IsBusy, "busy clears even when Prove It reports a handled failure");
    }

    [TestMethod]
    public async Task ProveIt_thrown_exception_surfaces_error_without_crashing()
    {
        var fake = new FakeOptimizeService { ThrowOnProveIt = true };
        var vm = new OptimizeViewModel(fake);

        await vm.ProveItAsync();

        Assert.IsFalse(vm.LastOk);
        Assert.IsFalse(vm.IsBusy);
        StringAssert.Contains(vm.StatusMessage, "failed");
    }

    [TestMethod]
    public async Task ProveIt_button_disables_while_capturing_and_re_enables_when_done()
    {
        var fake = new FakeOptimizeService();
        fake.ProveItRelease.Reset(); // hold ProveItAsync open

        var vm = new OptimizeViewModel(fake);
        var running = vm.ProveItAsync();
        Assert.IsTrue(fake.ProveItEntered.Wait(2000), "the action should have started");

        Assert.IsTrue(vm.IsBusy);
        Assert.IsFalse(vm.ProveItCommand.CanExecute(null), "Prove it disables while capturing");
        Assert.IsFalse(vm.GamingCommand.CanExecute(null), "Gaming/Restore also disable during Prove it -- one action in flight at a time");

        fake.ProveItRelease.Set();
        await running;

        Assert.IsTrue(vm.ProveItCommand.CanExecute(null), "Prove it re-enables when done");
    }
}
