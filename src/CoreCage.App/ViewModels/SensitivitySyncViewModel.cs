using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using CoreCage.Core.GameTune;

namespace CoreCage.App.ViewModels
{
    /// <summary>One row of the Sensitivity Sync strip: a tunable game's computed target sensitivity
    /// (+ display cm/360) and the outcome of the last sync attempt. Implements INotifyPropertyChanged
    /// — same accessibility lesson as <see cref="GamePresetCardViewModel"/> (Task 9) — so
    /// <see cref="SensitivitySyncViewModel.SyncAll"/> can update StatusText on the existing row objects
    /// in place, instead of the VM rebuilding the whole Rows list and the UI tearing down/rebuilding
    /// the ItemsControl containers (which would blow away keyboard focus on the Sync button).</summary>
    public sealed class SensitivityRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public required string DisplayName { get; init; }

        // TargetSens/Cm360 are set once at row-creation time (Recompute rebuilds the whole Rows list
        // when the reference/sens/DPI inputs change) and never mutate afterward — only StatusText
        // changes post-construction (via SyncAll), so only StatusText needs INPC plumbing.
        public required double TargetSens { get; init; }
        public required double Cm360 { get; init; }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value) return;
                _statusText = value;
                OnPropertyChanged();
            }
        }

        internal DetectedGame Game { get; init; } = null!;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Backs the Sensitivity Sync strip: given a reference game + sensitivity, computes the
    /// equivalent-feel value (and cm/360) for every detected game that has a yaw coefficient, and
    /// writes them through the GameTune safety gate on demand. Implements INotifyPropertyChanged for
    /// <see cref="Rows"/> only — Recompute() legitimately replaces the whole row set (a different
    /// reference game/sens/DPI means a different computation), so the ItemsControl bound to
    /// Sensitivity.Rows needs the change notification to re-pull the new list. SyncAll() does NOT
    /// touch this list — it mutates StatusText on the already-bound SensitivityRow instances, which
    /// those rows notify for themselves, so the ItemsControl containers (and any focus within them)
    /// are never rebuilt by a sync.</summary>
    public sealed class SensitivitySyncViewModel : INotifyPropertyChanged
    {
        private readonly GameTuneService _svc;
        private readonly IReadOnlyList<DetectedGame> _games;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string ReferenceGameId { get; set; } = "";
        public double ReferenceSens { get; set; } = 6.15;
        public int Dpi { get; set; } = 800;

        private IReadOnlyList<SensitivityRow> _rows = new List<SensitivityRow>();
        public IReadOnlyList<SensitivityRow> Rows
        {
            get => _rows;
            private set
            {
                _rows = value;
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<DetectedGame> TunableGames => _games.Where(g => g.Sensitivity is not null).ToList();

        public SensitivitySyncViewModel(GameTuneService svc, IReadOnlyList<DetectedGame> games)
        {
            _svc = svc; _games = games;
        }

        public void Recompute()
        {
            var reference = _games.FirstOrDefault(g => g.GameId == ReferenceGameId);
            var srcYaw = reference?.Sensitivity?.Yaw;
            var rows = new List<SensitivityRow>();
            foreach (var g in _games)
            {
                if (g.Sensitivity is null || srcYaw is null) continue;
                var target = SensitivityConverter.Convert(ReferenceSens, srcYaw.Value, g.Sensitivity.Yaw);
                rows.Add(new SensitivityRow
                {
                    DisplayName = g.DisplayName,
                    TargetSens = target,
                    Cm360 = SensitivityConverter.Cm360(target, g.Sensitivity.Yaw, Dpi),
                    Game = g
                });
            }
            Rows = rows;
        }

        public void SyncAll()
        {
            foreach (var row in Rows)
            {
                var g = row.Game;
                if (g.Graphics is null || g.Sensitivity is null) { row.StatusText = "No config."; continue; }
                var r = _svc.ApplySensitivity(g.GameId, g.ExeName, g.Graphics, g.Sensitivity, row.TargetSens);
                row.StatusText = r.Message;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
