using System.Collections.Generic;
using System.Linq;
using CoreCage.Core.GameTune;

namespace CoreCage.App.ViewModels
{
    /// <summary>Backing model for the Game Presets page: one card per detected game.</summary>
    public sealed class GamePresetsViewModel
    {
        public IReadOnlyList<GamePresetCardViewModel> Cards { get; }
        public bool IsEmpty => Cards.Count == 0;

        /// <summary>Backs the Sensitivity Sync strip shown above the cards — same DataContext (this
        /// VM), same GameTuneService + detected-games list the cards were built from.</summary>
        public SensitivitySyncViewModel Sensitivity { get; }

        public GamePresetsViewModel(GameTuneService svc, IReadOnlyList<DetectedGame> detected)
        {
            Cards = detected.Select(g => new GamePresetCardViewModel(svc, g)).ToList();
            Sensitivity = new SensitivitySyncViewModel(svc, detected);
        }
    }
}
