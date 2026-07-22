using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.App;

namespace CoreCage.Tests;

/// <summary>
/// The shell is the compact 8-group tree (Game Presets added alongside the original seven). These
/// prove its navigation brain: it exposes all eight sections, defaults to Optimize, swaps the
/// content VM when a section is selected, and that every group resolves to a real page (no
/// placeholders left).
/// </summary>
[TestClass]
public sealed class ShellViewModelTests
{
    private static ShellViewModel NewShell() =>
        new(new OptimizeViewModel(new FakeOptimizeService()),
            new MonitorViewModel(new FakeMonitorService()),
            new TuneViewModel(new FakeTuneService()),
            new SystemViewModel(new FakeSystemService()),
            new ProcessesViewModel(new FakeProcessService()),
            new ProfilesViewModel(new FakeProfileService()),
            new SettingsViewModel());

    [TestMethod]
    public void Exposes_all_eight_sections()
    {
        Assert.AreEqual(8, NewShell().Sections.Count);
    }

    [TestMethod]
    public void Defaults_to_Optimize_selected()
    {
        var shell = NewShell();
        Assert.AreEqual("optimize", shell.SelectedSection.Key);
        Assert.IsInstanceOfType(shell.CurrentContent, typeof(OptimizeViewModel));
    }

    [TestMethod]
    public void Selecting_Monitor_shows_the_monitor_vm()
    {
        var shell = NewShell();
        shell.SelectedSection = shell.SectionByKey("monitor");
        Assert.IsInstanceOfType(shell.CurrentContent, typeof(MonitorViewModel));
    }

    [TestMethod]
    public void Nav_sections_expose_their_human_title_as_accessible_name()
    {
        // ToString is what UI Automation / screen readers read for a ListItem.
        var shell = NewShell();
        Assert.AreEqual("Optimize", shell.SectionByKey("optimize").ToString());
        Assert.AreEqual("Monitor", shell.SectionByKey("monitor").ToString());
    }

    [TestMethod]
    public void Selecting_Tune_shows_the_real_tune_vm()
    {
        var shell = NewShell();
        shell.SelectedSection = shell.SectionByKey("tune");
        Assert.IsInstanceOfType(shell.CurrentContent, typeof(TuneViewModel));
    }

    [TestMethod]
    public void All_eight_groups_resolve_to_a_real_page_no_placeholders_left()
    {
        var shell = NewShell();
        foreach (var s in shell.Sections)
        {
            shell.SelectedSection = s;
            Assert.IsNotNull(shell.CurrentContent, $"'{s.Key}' should resolve to a real VM, not a null/blank page");
        }
    }

    [TestMethod]
    public void Only_the_visible_group_polls_hidden_tabs_stop_hitting_hardware()
    {
        var shell = NewShell();

        shell.SelectedSection = shell.SectionByKey("monitor");
        var mon = shell.CurrentContent as IPollingGroup;
        Assert.IsNotNull(mon);
        Assert.IsTrue(mon!.IsPolling, "the visible group must poll");

        shell.SelectedSection = shell.SectionByKey("optimize");
        Assert.IsFalse(mon.IsPolling, "leaving a tab must stop its background polling");
    }
}
