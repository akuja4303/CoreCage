using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.App;
using CoreCage.App.Services;

namespace CoreCage.Tests;

/// <summary>
/// The Monitor group reads real hardware through the in-process engine. These prove the VM's
/// honest states: it initializes the backend, formats a reading, shows a dash for a dead sensor
/// (never a fake 0°C), and surfaces an error instead of crashing when the backend throws.
/// </summary>
[TestClass]
public sealed class MonitorViewModelTests
{
    [TestMethod]
    public void Initializes_backend_and_reads_on_construction()
    {
        var fake = new FakeMonitorService();
        using var vm = new MonitorViewModel(fake);

        Assert.AreEqual(1, fake.InitializeCalls, "backend must be initialized exactly once");
        Assert.IsTrue(fake.ReadCalls >= 1, "an initial read must populate the screen");
    }

    [TestMethod]
    public void Populates_texts_from_a_reading()
    {
        var fake = new FakeMonitorService { Next = new HardwareReadout(55f, 61f, 42.0, "Ryzen 5 5600G", "RTX 3060") };
        using var vm = new MonitorViewModel(fake);

        vm.RefreshNow();

        Assert.AreEqual("55.0°C", vm.CpuTempText);
        Assert.AreEqual("61.0°C", vm.GpuTempText);
        Assert.AreEqual("42% used", vm.RamText);
        Assert.AreEqual("Ryzen 5 5600G", vm.CpuName);
        Assert.AreEqual("RTX 3060", vm.GpuName);
        Assert.IsFalse(vm.IsError);
    }

    [TestMethod]
    public void Dead_temp_sensor_shows_dash_not_zero()
    {
        var fake = new FakeMonitorService { Next = new HardwareReadout(null, 61f, 42.0, "CPU", "GPU") };
        using var vm = new MonitorViewModel(fake);

        vm.RefreshNow();

        Assert.AreEqual("—", vm.CpuTempText);
        Assert.IsFalse(vm.CpuTempText.Contains("0.0"), "a missing sensor must not fabricate 0.0°C");
    }

    [TestMethod]
    public void Read_failure_surfaces_an_error_state_not_a_crash()
    {
        var fake = new FakeMonitorService { ThrowOnRead = true };
        using var vm = new MonitorViewModel(fake);

        vm.RefreshNow();

        Assert.IsTrue(vm.IsError, "a backend failure must flip the error state");
        Assert.AreEqual("—", vm.CpuTempText, "no stale/fake value when the read failed");
    }
}
