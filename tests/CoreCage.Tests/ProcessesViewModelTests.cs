using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.App;

namespace CoreCage.Tests;

[TestClass]
public sealed class ProcessesViewModelTests
{
    [TestMethod]
    public void Refresh_populates_the_list()
    {
        var vm = new ProcessesViewModel(new FakeProcessService());
        Assert.AreEqual(3, vm.Processes.Count);
        Assert.IsFalse(vm.IsEmpty);
        Assert.IsTrue(vm.HasProcesses);
    }

    [TestMethod]
    public void Empty_list_shows_honest_empty_state()
    {
        // FOLD (Task 8 UX pass): a zero-row list (e.g. a permission failure) must never render as a
        // silent blank panel -- mirrors ProfilesViewModel's IsEmpty/HasProfiles pattern.
        var vm = new ProcessesViewModel(new FakeProcessService { Procs = new() });
        Assert.IsTrue(vm.IsEmpty);
        Assert.IsFalse(vm.HasProcesses);
        StringAssert.Contains(vm.StatusMessage.ToLowerInvariant(), "no processes found");
    }

    // ------------------------------------------------------------------
    // Three-state StatusKind (review IMPORTANT-1): the empty/populated process-count readout is
    // informational, not a completed action's outcome -- it must render Neutral (no ✓), never a false
    // green success. A completed Kill flips to Success/Error.
    // ------------------------------------------------------------------

    [TestMethod]
    public void Default_status_is_Neutral_not_Success()
    {
        var vm = new ProcessesViewModel(new FakeProcessService());
        Assert.AreEqual(StatusKind.Neutral, vm.StatusKind, "the process-count readout is informational, not a success");
    }

    [TestMethod]
    public void Empty_state_status_is_Neutral()
    {
        var vm = new ProcessesViewModel(new FakeProcessService { Procs = new() });
        Assert.AreEqual(StatusKind.Neutral, vm.StatusKind, "'No processes found' is informational, not a failure or success");
    }

    [TestMethod]
    public async Task Successful_kill_sets_StatusKind_Success()
    {
        var fake = new FakeProcessService();
        var vm = new ProcessesViewModel(fake) { Selected = new CoreCage.App.Services.ProcInfo(1001, "game", 1600) };

        await vm.KillSelectedAsync();

        Assert.AreEqual(StatusKind.Success, vm.StatusKind);
    }

    [TestMethod]
    public async Task Failed_kill_sets_StatusKind_Error()
    {
        var fake = new FakeProcessService { KillResult = false };
        var vm = new ProcessesViewModel(fake) { Selected = new CoreCage.App.Services.ProcInfo(1001, "game", 1600) };

        await vm.KillSelectedAsync();

        Assert.AreEqual(StatusKind.Error, vm.StatusKind);
    }

    [TestMethod]
    public async Task Kill_with_no_selection_is_a_safe_hint()
    {
        var fake = new FakeProcessService();
        var vm = new ProcessesViewModel(fake) { Selected = null };
        await vm.KillSelectedAsync();
        Assert.IsNull(fake.LastKilledPid, "nothing killed when nothing selected");
        StringAssert.Contains(vm.StatusMessage.ToLowerInvariant(), "select");
    }

    [TestMethod]
    public async Task Kill_selected_calls_service_with_its_pid()
    {
        var fake = new FakeProcessService();
        var vm = new ProcessesViewModel(fake);
        vm.Selected = new CoreCage.App.Services.ProcInfo(1001, "game", 1600);
        await vm.KillSelectedAsync();
        Assert.AreEqual(1001, fake.LastKilledPid);
        Assert.IsTrue(vm.LastOk);
    }

    [TestMethod]
    public async Task Failed_kill_reports_honestly()
    {
        var fake = new FakeProcessService { KillResult = false };
        var vm = new ProcessesViewModel(fake);
        vm.Selected = new CoreCage.App.Services.ProcInfo(1001, "game", 1600);
        await vm.KillSelectedAsync();
        Assert.IsFalse(vm.LastOk);
    }
}
