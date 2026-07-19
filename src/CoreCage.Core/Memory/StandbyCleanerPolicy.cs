namespace CoreCage.Core.Memory
{
    /// <summary>
    /// ISLC-style trigger policy (pure, unit-testable). Purge the standby list when free RAM drops
    /// below <see cref="FreeThresholdMb"/>, or when the standby cache grows past
    /// <see cref="StandbyThresholdMb"/>. Defaults suit a large-RAM rig (64 GB) — tune per machine.
    /// </summary>
    public class StandbyCleanerPolicy
    {
        public bool Enabled { get; set; } = true;
        public int FreeThresholdMb { get; set; } = 2048;
        public int StandbyThresholdMb { get; set; } = 8192;

        /// <summary>availableMb/standbyMb &lt; 0 mean "unknown" and are ignored by that branch.</summary>
        public bool ShouldPurge(long availableMb, long standbyMb)
        {
            if (!Enabled) return false;
            if (availableMb >= 0 && availableMb < FreeThresholdMb) return true;
            if (standbyMb >= 0 && standbyMb > StandbyThresholdMb) return true;
            return false;
        }
    }
}
