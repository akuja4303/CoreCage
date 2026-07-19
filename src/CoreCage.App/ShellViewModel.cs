using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CoreCage.App;

/// <summary>One entry in the left navigation rail.</summary>
public sealed class NavSection
{
    public NavSection(string key, string title, string glyph)
    {
        Key = key;
        Title = title;
        Glyph = glyph;
    }

    public string Key { get; }
    public string Title { get; }
    /// <summary>A leading emoji/glyph for the rail — a quick visual anchor per section.</summary>
    public string Glyph { get; }

    /// <summary>Screen readers and UI Automation use this as the item's accessible name — return the
    /// human title ("Optimize"), not the type name. Also lets automation target sections by name.</summary>
    public override string ToString() => Title;
}

/// <summary>
/// The compact 7-group tree. Owns the navigation rail and swaps the content VM when a section is
/// selected. Every section is wired to the real in-process engine. No engine logic here — it just
/// routes to the group VMs.
/// </summary>
public sealed class ShellViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Dictionary<string, object> _contentByKey;
    private readonly MonitorViewModel _monitor;
    private readonly TuneViewModel _tune;
    private readonly SystemViewModel _system;

    public ShellViewModel()
        : this(new OptimizeViewModel(), new MonitorViewModel(), new TuneViewModel(),
               new SystemViewModel(), new ProcessesViewModel(), new ProfilesViewModel(), new SettingsViewModel())
    {
    }

    public ShellViewModel(OptimizeViewModel optimize, MonitorViewModel monitor, TuneViewModel tune,
                          SystemViewModel system, ProcessesViewModel processes, ProfilesViewModel profiles,
                          SettingsViewModel settings)
    {
        _monitor = monitor;
        _tune = tune;
        _system = system;
        Sections = new ObservableCollection<NavSection>
        {
            new("optimize",  "Optimize",  "⚡"),
            new("monitor",   "Monitor",   "📊"),
            new("tune",      "Tune",      "🎛"),
            new("system",    "System",    "🧹"),
            new("processes", "Processes", "📋"),
            new("profiles",  "Profiles",  "💾"),
            new("settings",  "Settings",  "⚙"),
        };

        _contentByKey = new Dictionary<string, object>
        {
            ["optimize"]  = optimize,
            ["monitor"]   = monitor,
            ["tune"]      = tune,
            ["system"]    = system,
            ["processes"] = processes,
            ["profiles"]  = profiles,
            ["settings"]  = settings,
        };

        // Each timer-backed VM starts polling in its own ctor; stop them all up front so only the
        // group we select below (and thereafter) polls. Optimize is first and doesn't poll → silent start.
        foreach (var vm in _contentByKey.Values)
            if (vm is IPollingGroup g) g.Deactivate();

        SelectedSection = Sections[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<NavSection> Sections { get; }

    /// <summary>Finds a section by key (used by the shell + tests to navigate programmatically).</summary>
    public NavSection SectionByKey(string key) => Sections.First(s => s.Key == key);

    private NavSection? _selectedSection;
    public NavSection? SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (Set(ref _selectedSection, value))
                CurrentContent = value != null ? _contentByKey[value.Key] : null;
        }
    }

    private object? _currentContent;
    /// <summary>The VM the content area binds to; a DataTemplate maps it to the right page. Swapping
    /// content stops the outgoing group's polling and starts the incoming one's, so only the visible
    /// tab hits hardware.</summary>
    public object? CurrentContent
    {
        get => _currentContent;
        private set
        {
            if (ReferenceEquals(_currentContent, value)) return;
            if (_currentContent is IPollingGroup outgoing) outgoing.Deactivate();
            _currentContent = value;
            if (value is IPollingGroup incoming) incoming.Activate();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentContent)));
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    /// <summary>Stops the polling timers (Monitor + Tune) when the window closes.</summary>
    public void Dispose()
    {
        _monitor.Dispose();
        _tune.Dispose();
        _system.Dispose();
    }
}
