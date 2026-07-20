using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using CoreCage.App.Services;

namespace CoreCage.App;

/// <summary>
/// One Tweak Ledger row, display-ready for the Optimize page's Ledger card: a human tweak name,
/// whether it's active, and either the measured delta ("FPS +12.3, 1% lows +8.1") or "not yet
/// benchmarked" when no Prove It run has filled the numbers in yet.
/// </summary>
public sealed record LedgerRowViewModel(string DisplayName, bool Active, string DeltaText);

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
        ProveItCommand = new RelayCommand(ProveItAsync, CanRun);
        RefreshGamingIsActive();
        RefreshLedgerRows();

        LogicalCoreCount = Math.Max(_svc.LogicalCoreCount, 1);
        AvailableReservedCoreCounts = BuildAvailableReservedCoreCounts(LogicalCoreCount);
        _coreCageEnabled = _svc.ReadCoreCageEnabled();
        _coreCageReservedCores = ClampReservedCores(_svc.ReadCoreCageReservedCores());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand GamingCommand { get; }
    public RelayCommand RestoreCommand { get; }
    /// <summary>Runs the A/B "prove it" benchmark (bench → re-apply → bench) against the currently
    /// active tweaks and updates the Ledger card with the measured delta.</summary>
    public RelayCommand ProveItCommand { get; }

    /// <summary>Tweak Ledger rows for the Optimize page's Ledger card — refreshed after every
    /// Apply/Restore/Prove-it completes (same "read once, not on a poll" posture as
    /// <see cref="GamingIsActive"/>).</summary>
    public ObservableCollection<LedgerRowViewModel> LedgerRows { get; } = new();

    private bool _isLedgerEmpty = true;
    /// <summary>True when no tweak has ever been applied — drives the "No tweaks applied yet — hit
    /// Gaming Mode" empty state.</summary>
    public bool IsLedgerEmpty
    {
        get => _isLedgerEmpty;
        private set
        {
            if (Set(ref _isLedgerEmpty, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLedgerRows)));
        }
    }

    /// <summary>Inverse of <see cref="IsLedgerEmpty"/>, for the Ledger card's row-list visibility.</summary>
    public bool HasLedgerRows => !IsLedgerEmpty;

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

    private StatusKind _statusKind = StatusKind.Neutral;
    /// <summary>Drives the status bar's brush+glyph (review IMPORTANT-1). Neutral on construction
    /// ("Pick a mode.") and while an action is running; Success/Error only once an action has actually
    /// completed — a bool LastOk had no way to express "nothing happened yet".</summary>
    public StatusKind StatusKind { get => _statusKind; private set => Set(ref _statusKind, value); }

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
    internal Task ProveItAsync()     => RunAsync(p => _svc.ProveItAsync(p),     "Prove it");

    private async Task RunAsync(Func<IProgress<string>?, Task<OptimizeResult>> action, string label)
    {
        IsBusy = true;
        StatusMessage = $"Applying {label}…";
        StatusKind = StatusKind.Neutral; // in progress — not a result yet, so no ✓/✗
        try
        {
            var progress = new Progress<string>(msg => StatusMessage = msg);
            var result = await Task.Run(() => action(progress)).ConfigureAwait(true);
            LastOk = result.Ok;
            StatusKind = result.Ok ? StatusKind.Success : StatusKind.Error;
            StatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            LastOk = false;
            StatusKind = StatusKind.Error;
            StatusMessage = $"{label} failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            RefreshGamingIsActive();
            RefreshLedgerRows();
        }
    }

    private void RefreshGamingIsActive() => GamingIsActive = _svc.ReadGamingIsActive();

    private void RefreshLedgerRows()
    {
        LedgerRows.Clear();
        var rows = _svc.ReadLedgerRows();
        bool wholeStackMeasured = rows.Any(r => r.TweakId == WholeStackTweakId && IsFullyMeasured(r));
        foreach (var row in rows)
            LedgerRows.Add(new LedgerRowViewModel(LedgerDisplayName(row.TweakId), row.Active, LedgerDeltaText(row, wholeStackMeasured)));
        IsLedgerEmpty = LedgerRows.Count == 0;
    }

    /// <summary>TweakId for the single whole-stack row Prove It records to. Sourced from the shared
    /// <see cref="CoreCage.Core.Ledger.TweakIds.GamingStack"/> constant (mirrors
    /// <see cref="EngineOptimizeService.WholeStackTweakId"/>) so the "gaming-stack" literal isn't
    /// duplicated across the two classes (review MINOR finding).</summary>
    internal const string WholeStackTweakId = CoreCage.Core.Ledger.TweakIds.GamingStack;

    /// <summary>Human name for a TweakId — pure, unit-testable without a service.</summary>
    internal static string LedgerDisplayName(string tweakId) => tweakId switch
    {
        "gaming-pipeline" => "Gaming Mode++",
        "eac-polish" => "EAC-safe polish",
        "core-unpark" => "Core-unpark",
        "core-cage" => "Core Cage",
        WholeStackTweakId => "Gaming Mode (whole stack)",
        _ => tweakId,
    };

    /// <summary>
    /// The whole-stack row (<see cref="WholeStackTweakId"/>) shows the real measured delta, e.g.
    /// "FPS +12.3, 1% lows +8.1", once ALL FOUR benchmark fields are present (guarding every field,
    /// not just Fps — a null 1%-low must never render as "+0.0" as if it were measured; review MINOR-5).
    /// Every other (per-tweak step) row can never carry a delta of its own — a single whole-stack A/B
    /// can't attribute causation to an individual tweak (review CRITICAL-2) — so an Active step row
    /// shows "measured as part of whole stack" once the whole-stack row has numbers, and "not yet
    /// benchmarked" otherwise. Pure — unit-testable without a service.
    /// </summary>
    internal static string LedgerDeltaText(LedgerRowInfo row, bool wholeStackMeasured)
    {
        if (row.TweakId == WholeStackTweakId)
        {
            if (!IsFullyMeasured(row)) return "not yet benchmarked";
            double fpsDelta = row.AfterFps!.Value - row.BaselineFps!.Value;
            double p1Delta = row.AfterOnePctLow!.Value - row.BaselineOnePctLow!.Value;
            return $"FPS {Signed(fpsDelta)}, 1% lows {Signed(p1Delta)}";
        }

        if (row.Active && wholeStackMeasured) return "measured as part of whole stack";
        return "not yet benchmarked";
    }

    /// <summary>All four benchmark fields must be non-null before a row's numbers are trusted as a real
    /// measurement (review MINOR-5) — a row with, say, AfterFps but no BaselineOnePctLow is not fully
    /// measured and must never render a delta.</summary>
    private static bool IsFullyMeasured(LedgerRowInfo row) =>
        row.BaselineFps != null && row.BaselineOnePctLow != null && row.AfterFps != null && row.AfterOnePctLow != null;

    private static string Signed(double v) => (v >= 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture);

    private void RaiseCanExecuteChanged()
    {
        GamingCommand.RaiseCanExecuteChanged();
        RestoreCommand.RaiseCanExecuteChanged();
        ProveItCommand.RaiseCanExecuteChanged();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
