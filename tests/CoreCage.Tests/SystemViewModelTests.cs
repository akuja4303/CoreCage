using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.App;

namespace CoreCage.Tests;

[TestClass]
public sealed class SystemViewModelTests
{
    [TestMethod]
    public void Populates_ram_readout()
    {
        using var vm = new SystemViewModel(new FakeSystemService());
        StringAssert.Contains(vm.RamText, "18.0");
        StringAssert.Contains(vm.RamText, "64.0");
        StringAssert.Contains(vm.AvailableText, "free");
    }

    [TestMethod]
    public async Task Free_working_sets_calls_service_and_reports()
    {
        var fake = new FakeSystemService();
        using var vm = new SystemViewModel(fake);
        await vm.FreeWorkingSetsAsync();
        Assert.AreEqual(1, fake.FreeCalls);
        Assert.IsTrue(vm.LastOk);
        StringAssert.Contains(vm.StatusMessage.ToLowerInvariant(), "freed");
    }

    [TestMethod]
    public async Task Clear_standby_calls_service()
    {
        var fake = new FakeSystemService();
        using var vm = new SystemViewModel(fake);
        await vm.ClearStandbyAsync();
        Assert.AreEqual(1, fake.StandbyCalls);
        Assert.IsTrue(vm.LastOk);
    }

    [TestMethod]
    public async Task Failed_action_reports_failure()
    {
        var fake = new FakeSystemService { WorkingSetsResult = false };
        using var vm = new SystemViewModel(fake);
        await vm.FreeWorkingSetsAsync();
        Assert.IsFalse(vm.LastOk);
    }
}
