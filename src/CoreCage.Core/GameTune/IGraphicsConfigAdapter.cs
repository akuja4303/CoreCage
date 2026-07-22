using System.Collections.Generic;

namespace CoreCage.Core.GameTune
{
    /// <summary>Reads and writes one game-engine's graphics config format. Pure: Read/Plan never
    /// mutate anything; Write applies only the planned changes and preserves every other line.</summary>
    public interface IGraphicsConfigAdapter
    {
        string Format { get; }
        GraphicsReadResult Read(string configPath);
        GraphicsApplyPlan Plan(GraphicsReadResult current, IReadOnlyDictionary<string, string> preset);
        void Write(string configPath, GraphicsApplyPlan plan);
    }
}
