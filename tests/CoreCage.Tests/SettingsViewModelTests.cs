using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.App;

namespace CoreCage.Tests;

[TestClass]
public sealed class SettingsViewModelTests
{
    [TestMethod]
    public void Reports_app_identity_and_version()
    {
        var vm = new SettingsViewModel();
        Assert.AreEqual("CoreCage", vm.AppName);
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.Version));
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.EngineText));
    }

    [TestMethod]
    public void Elevation_text_matches_the_flag()
    {
        var vm = new SettingsViewModel();
        // Test host isn't elevated → the text must say so honestly (never claim elevated when not).
        if (vm.IsElevated) StringAssert.Contains(vm.ElevationText, "elevated");
        else StringAssert.Contains(vm.ElevationText.ToLowerInvariant(), "not elevated");
    }
}
