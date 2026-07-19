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
