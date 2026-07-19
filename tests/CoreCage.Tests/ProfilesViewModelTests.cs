using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.App;

namespace CoreCage.Tests;

[TestClass]
public sealed class ProfilesViewModelTests
{
    [TestMethod]
    public void Lists_profiles_and_reports_count()
    {
        var vm = new ProfilesViewModel(new FakeProfileService());
        Assert.AreEqual(2, vm.Profiles.Count);
        Assert.IsTrue(vm.HasProfiles);
        Assert.IsFalse(vm.IsEmpty);
    }

    [TestMethod]
    public void Empty_list_shows_honest_empty_state()
    {
        var vm = new ProfilesViewModel(new FakeProfileService { Items = new() });
        Assert.IsTrue(vm.IsEmpty);
        StringAssert.Contains(vm.StatusMessage.ToLowerInvariant(), "no saved profiles");
    }

    [TestMethod]
    public async Task Apply_selected_calls_service()
    {
        var fake = new FakeProfileService();
        var vm = new ProfilesViewModel(fake);
        vm.Selected = new CoreCage.App.Services.ProfileInfo("Esports", "");
        await vm.ApplySelectedAsync();
        Assert.AreEqual("Esports", fake.LastApplied);
        Assert.IsTrue(vm.LastOk);
    }

    [TestMethod]
    public async Task Delete_selected_calls_service()
    {
        var fake = new FakeProfileService();
        var vm = new ProfilesViewModel(fake);
        vm.Selected = new CoreCage.App.Services.ProfileInfo("Quiet", "");
        await vm.DeleteSelectedAsync();
        Assert.AreEqual("Quiet", fake.LastDeleted);
    }

    [TestMethod]
    public async Task Apply_with_no_selection_is_a_safe_hint()
    {
        var fake = new FakeProfileService();
        var vm = new ProfilesViewModel(fake) { Selected = null };
        await vm.ApplySelectedAsync();
        Assert.IsNull(fake.LastApplied);
        StringAssert.Contains(vm.StatusMessage.ToLowerInvariant(), "select");
    }
}
