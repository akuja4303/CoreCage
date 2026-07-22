using System.Collections.Generic;
using CoreCage.Core.GameTune;

namespace CoreCage.App.ViewModels
{
    public enum CardState { Ready, Applied, GameRunning, NotSupported, ConfigNotFound, Error }

    /// <summary>A detected game plus the (optional) graphics preset context the UI needs.</summary>
    public sealed record DetectedGame(string GameId, string ExeName, string DisplayName, GraphicsBlock? Graphics);

    /// <summary>State machine for one game card: computes its state/affordances from the last
    /// GameTune result and drives Apply/Restore through the service.</summary>
    public sealed class GamePresetCardViewModel
    {
        private readonly GameTuneService _svc;
        private readonly DetectedGame _game;

        public string DisplayName => _game.DisplayName;
        public CardState State { get; private set; }
        public string StatusText { get; private set; } = "";
        public IReadOnlyList<GraphicsChange> LastChanges { get; private set; } = System.Array.Empty<GraphicsChange>();
        public string? BackupPath { get; private set; }

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
    }
}
