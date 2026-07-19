using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using CoreCage.App.Services;

namespace CoreCage.App;

/// <summary>
/// Drives the Optimize group: Gaming / Restore. CoreCage.App is gaming-only — there is no second
/// performance-mode surface. The mode action is heavy and blocking (it shells out to powercfg, the registry,
/// services), so it runs on a background thread while the UI shows a busy state and the buttons
/// disable. Results come back as an honest success/failure message; a thrown action becomes an error
/// message, never a crash.
/// </summary>
public sealed class OptimizeViewModel : INotifyPropertyChanged
{
    private readonly IOptimizeService _svc;

    public OptimizeViewModel() : this(new EngineOptimizeService())
    {
    }

    public OptimizeViewModel(IOptimizeService svc)
    {
        _svc = svc;
        GamingCommand = new RelayCommand(ApplyGamingAsync, CanRun);
        RestoreCommand = new RelayCommand(RestoreAsync, CanRun);
        RefreshGamingIsActive();

        LogicalCoreCount = Math.Max(_svc.LogicalCoreCount, 1);
        AvailableReservedCoreCounts = BuildAvailableReservedCoreCounts(LogicalCoreCount);
        _coreCageEnabled = _svc.ReadCoreCageEnabled();
        _coreCageReservedCores = ClampReservedCores(_svc.ReadCoreCageReservedCores());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand GamingCommand { get; }
    public RelayCommand RestoreCommand { get; }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                RaiseCanExecuteChanged();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEditCoreCageSettings)));
            }
        }
    }

    /// <summary>Whether the Core Cage toggle/picker should accept input right now. Rides the same
    /// busy-state pattern as the Gaming/Restore buttons — disabled while a mode action is in flight.</summary>
    public bool CanEditCoreCageSettings => !IsBusy;

    private string _statusMessage = "Pick a mode.";
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

    private bool _lastOk = true;
    public bool LastOk { get => _lastOk; private set => Set(ref _lastOk, value); }

    private bool _gamingIsActive;
    /// <summary>
    /// Whether Gaming Mode is currently active. <c>IModeModule.IsActive</c> does file I/O per read, so
    /// this is read once — on construction and again after an apply/restore completes — never bound
    /// directly to a polling/per-frame path.
    /// </summary>
    public bool GamingIsActive
    {
        get => _gamingIsActive;
        private set
        {
            if (Set(ref _gamingIsActive, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GamingStatusText)));
        }
    }

    /// <summary>Display string for <see cref="GamingIsActive"/> — what the Optimize page shows.</summary>
    public string GamingStatusText => GamingIsActive ? "ACTIVE" : "inactive";

    /// <summary>How many logical cores this machine has — bounds <see cref="AvailableReservedCoreCounts"/>.</summary>
    public int LogicalCoreCount { get; }

    /// <summary>Valid values for the reserved-cores picker: 1 .. LogicalCoreCount-1, so at least one
    /// core is always left over for the cage (enforced here, not just clamped on write).</summary>
    public IReadOnlyList<int> AvailableReservedCoreCounts { get; }

    private bool _coreCageEnabled;
    /// <summary>Core Cage on/off — reserve top cores for the game, confine background processes to the
    /// rest. Persisted through <see cref="IOptimizeService"/> (FeatureFlags.CoreCageEnabled) on change.</summary>
    public bool CoreCageEnabled
    {
        get => _coreCageEnabled;
        set
        {
            if (Set(ref _coreCageEnabled, value))
                _svc.WriteCoreCageEnabled(value);
        }
    }

    private int _coreCageReservedCores;
    /// <summary>How many logical cores Core Cage reserves for the game. Clamped to
    /// [1, LogicalCoreCount-1] so the cage always has at least one core to confine background processes
    /// to. Persisted through <see cref="IOptimizeService"/> (FeatureFlags.CoreCageReservedCores) on change.</summary>
    public int CoreCageReservedCores
    {
        get => _coreCageReservedCores;
        set
        {
            int clamped = ClampReservedCores(value);
            if (Set(ref _coreCageReservedCores, clamped))
                _svc.WriteCoreCageReservedCores(clamped);
        }
    }

    private int ClampReservedCores(int value)
    {
        int max = System.Math.Max(LogicalCoreCount - 1, 1);
        if (value < 1) return 1;
        if (value > max) return max;
        return value;
    }

    private static IReadOnlyList<int> BuildAvailableReservedCoreCounts(int logicalCoreCount)
    {
        int max = System.Math.Max(logicalCoreCount - 1, 1);
        return Enumerable.Range(1, max).ToList();
    }

    private bool CanRun() => !IsBusy;

    internal Task ApplyGamingAsync() => RunAsync(p => _svc.ApplyGamingAsync(p), "Gaming Mode");
    internal Task RestoreAsync()     => RunAsync(p => _svc.RestoreAsync(p),     "Restore");

    private async Task RunAsync(Func<IProgress<string>?, Task<OptimizeResult>> action, string label)
    {
        IsBusy = true;
        StatusMessage = $"Applying {label}…";
        try
        {
            var progress = new Progress<string>(msg => StatusMessage = msg);
            var result = await Task.Run(() => action(progress)).ConfigureAwait(true);
            LastOk = result.Ok;
            StatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            LastOk = false;
            StatusMessage = $"{label} failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            RefreshGamingIsActive();
        }
    }

    private void RefreshGamingIsActive() => GamingIsActive = _svc.ReadGamingIsActive();

    private void RaiseCanExecuteChanged()
    {
        GamingCommand.RaiseCanExecuteChanged();
        RestoreCommand.RaiseCanExecuteChanged();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
