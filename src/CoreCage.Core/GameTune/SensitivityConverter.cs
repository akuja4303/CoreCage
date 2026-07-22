namespace CoreCage.Core.GameTune
{
    /// <summary>Pure cross-game mouse-sensitivity math. Because the same mouse+DPI is used across
    /// games, matching aim feel (cm/360) reduces to the ratio of yaw coefficients — DPI cancels.</summary>
    public static class SensitivityConverter
    {
        /// <summary>Sensitivity in the target game that matches the source game's aim feel.</summary>
        public static double Convert(double sourceSens, double sourceYaw, double targetYaw)
            => sourceSens * sourceYaw / targetYaw;

        /// <summary>Centimetres of mouse travel for a 360 turn (display metric only).</summary>
        public static double Cm360(double sens, double yaw, int dpi)
            => (360.0 / (yaw * sens)) / (dpi / 2.54);
    }
}
