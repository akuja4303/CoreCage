using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.App;
using CoreCage.App.Services;

namespace CoreCage.Tests;

/// <summary>
/// The Tune group drives the real GPU. These prove: it degrades honestly on a non-NVIDIA rig,
/// populates the readout and seeds the input fields to current values, sends the input value on
/// apply, and — crucially — reports a silent NVAPI no-op as a failure rather than a fake success.
/// </summary>
[TestClass]
public sealed class TuneViewModelTests
{
    [TestMethod]
    public void Unavailable_gpu_disables_controls_and_says_so()
    {
        var fake = new FakeTuneService { Readout = GpuReadout.Unavailable };
        using var vm = new TuneViewModel(fake);

        Assert.IsFalse(vm.GpuAvailable);
        StringAssert.Contains(vm.StatusMessage.ToLowerInvariant(), "unavailable");
        Assert.IsFalse(vm.PowerLimitCommand.CanExecute(null));
        Assert.IsFalse(vm.VibranceCommand.CanExecute(null));
    }

    [TestMethod]
    public void Available_gpu_populates_readout_and_seeds_inputs()
    {
        var fake = new FakeTuneService();
        using var vm = new TuneViewModel(fake);

        Assert.IsTrue(vm.GpuAvailable);
        StringAssert.Contains(vm.CoreText, "1800");
        StringAssert.Contains(vm.PowerText, "120");
        Assert.AreEqual(170, vm.PowerLimitInput, "power input seeds to the current limit");
        Assert.AreEqual(50, vm.VibranceInput, "vibrance input seeds to the current level");
        Assert.IsTrue(vm.PowerLimitCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task Apply_power_limit_sends_the_input_watts()
    {
        var fake = new FakeTuneService();
        using var vm = new TuneViewModel(fake);

        vm.PowerLimitInput = 140;
        await vm.ApplyPowerLimitAsync();

        Assert.AreEqual(140, fake.LastPowerWatts);
        Assert.IsTrue(vm.LastOk);
    }

    [TestMethod]
    public async Task Apply_core_offset_sends_the_input_mhz()
    {
        var fake = new FakeTuneService();
        using var vm = new TuneViewModel(fake);

        vm.CoreOffsetInput = 120;
        await vm.ApplyCoreOffsetAsync();

        Assert.AreEqual(120, fake.LastOffsetMhz);
    }

    [TestMethod]
    public async Task Apply_vibrance_sends_the_input_level()
    {
        var fake = new FakeTuneService();
        using var vm = new TuneViewModel(fake);

        vm.VibranceInput = 63;
        await vm.ApplyVibranceAsync();

        Assert.AreEqual(63, fake.LastVibrance);
    }

    [TestMethod]
    public async Task Silent_noop_offset_is_reported_honestly_not_as_success()
    {
        var fake = new FakeTuneService { OffsetResult = false };
        using var vm = new TuneViewModel(fake);

        vm.CoreOffsetInput = 100;
        await vm.ApplyCoreOffsetAsync();

        Assert.IsFalse(vm.LastOk, "a failed/no-op offset must not read as success");
        StringAssert.Contains(vm.StatusMessage.ToLowerInvariant(), "offset");
    }
}
