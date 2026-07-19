namespace CoreCage.Core.Thermal
{
    /// <summary>What the thermal guard should do this tick.</summary>
    public enum ThermalAction { None, Engage, Sustain, Release }

    /// <summary>Pure hysteresis decision for the CPU thermal guard — keeps it from flapping at the
    /// threshold. Engage at/above High; once engaged, hold (Sustain) until temp falls to/below
    /// Release, then Release. A non-positive reading (sensor blip) never flips state. Unit-tested.</summary>
    public static class ThermalGuardPolicy
    {
        public static ThermalAction Decide(double tempC, double highC, double releaseC, bool engaged)
        {
            if (tempC <= 0)                       // bad/no reading — don't change state on noise
                return engaged ? ThermalAction.Sustain : ThermalAction.None;

            if (!engaged)
                return tempC >= highC ? ThermalAction.Engage : ThermalAction.None;

            return tempC <= releaseC ? ThermalAction.Release : ThermalAction.Sustain;
        }
    }
}
