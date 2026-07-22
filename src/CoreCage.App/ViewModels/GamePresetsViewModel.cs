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

        public GamePresetsViewModel(GameTuneService svc, IReadOnlyList<DetectedGame> detected)
        {
            Cards = detected.Select(g => new GamePresetCardViewModel(svc, g)).ToList();
        }
    }
}
