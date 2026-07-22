using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CoreCage.Core.GameTune;

namespace CoreCage.App.ViewModels
{
    public enum CardState { Ready, Applied, GameRunning, NotSupported, ConfigNotFound, Error }

    /// <summary>A detected game plus the (optional) graphics preset context the UI needs, and the
    /// (optional) mouse-sensitivity context the Sensitivity Sync strip needs.</summary>
    public sealed record DetectedGame(string GameId, string ExeName, string DisplayName, GraphicsBlock? Graphics,
        SensitivityBlock? Sensitivity);

    /// <summary>State machine for one game card: computes its state/affordances from the last
    /// GameTune result and drives Apply/Restore through the service. Implements INotifyPropertyChanged
    /// so the UI updates in place (no ItemsControl container rebuild, which would blow away keyboard
    /// focus on the button the user just clicked).</summary>
    public sealed class GamePresetCardViewModel : INotifyPropertyChanged
    {
        private readonly GameTuneService _svc;
        private readonly DetectedGame _game;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string DisplayName => _game.DisplayName;

        private CardState _state;
        public CardState State
        {
            get => _state;
            private set
            {
                if (_state == value) return;
                _state = value;
                OnPropertyChanged();
                // CanApply/CanRestore are derived from State — no independent backing field, so they
                // need their own notification whenever State changes.
                OnPropertyChanged(nameof(CanApply));
                OnPropertyChanged(nameof(CanRestore));
            }
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText == value) return;
                _statusText = value;
                OnPropertyChanged();
            }
        }

        private IReadOnlyList<GraphicsChange> _lastChanges = Array.Empty<GraphicsChange>();
        public IReadOnlyList<GraphicsChange> LastChanges
        {
            get => _lastChanges;
            private set
            {
                _lastChanges = value;
                OnPropertyChanged();
            }
        }

        private string? _backupPath;
        public string? BackupPath
        {
            get => _backupPath;
            private set
            {
                if (_backupPath == value) return;
                _backupPath = value;
                OnPropertyChanged();
            }
        }

        public bool CanApply => State is CardState.Ready or CardState.ConfigNotFound or CardState.Error;
        public bool CanRestore => State is CardState.Applied;

        public GamePresetCardViewModel(GameTuneService svc, DetectedGame game)
        {
            _svc = svc; _game = game;
            ComputeInitialState();
        }

        public void Apply() => Absorb(_svc.Apply(_game.GameId, _game.ExeName, _game.Graphics));
        public void Restore() => Absorb(_svc.Restore(_game.GameId, _game.ExeName, _game.Graphics));

        /// <summary>Recomputes the card's initial state (e.g. after a transient failure like
        /// GameRunning/ConfigNotFound/Error) so the user can retry Apply without rebuilding the VM.
        /// Preserves BackupPath — does not clear the last known backup location.</summary>
        public void Refresh() => ComputeInitialState();

        private void ComputeInitialState()
        {
            State = (_game.Graphics is null || _game.Graphics.GuidedOnly) ? CardState.NotSupported : CardState.Ready;
            StatusText = State == CardState.NotSupported ? "Guided only — no auto-apply." : "Ready to apply Max-FPS.";
        }

        private void Absorb(GameTuneResult r)
        {
            StatusText = r.Message;
            LastChanges = r.Changes;
            BackupPath = r.BackupPath ?? BackupPath;
            State = r.Status switch
            {
                GameTuneStatus.Applied      => CardState.Applied,
                GameTuneStatus.Restored     => CardState.Ready,
                GameTuneStatus.GameRunning  => CardState.GameRunning,
                GameTuneStatus.NotSupported => CardState.NotSupported,
                GameTuneStatus.ConfigNotFound => CardState.ConfigNotFound,
                GameTuneStatus.UnsafePath   => CardState.NotSupported,
                _                           => CardState.Error
            };
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
