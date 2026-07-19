using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.Telemetry;

namespace CoreCage.Tests
{
    /// <summary>
    /// Batch A4 (security/DoS hardening, 2026-07-03 audit): the /benchmark API passed an
    /// unbounded <c>sec</c> straight into the capture — <c>sec=999999</c> blocks a thread-pool
    /// thread for ~11 days, and <c>sec=2000000000</c> overflows the (sec+15)*1000 ms timeout to a
    /// negative that makes WaitForExit throw before the kill runs, orphaning an elevated PresentMon
    /// child. The capture now clamps to [1,300]s at the choke point.
    /// </summary>
    [TestClass]
    public class PresentMonCaptureClampTests
    {
        [TestMethod]
        public void ClampCaptureSeconds_BoundsToSaneWindow()
        {
            Assert.AreEqual(1, PresentMonInterface.ClampCaptureSeconds(0), "floor to 1");
            Assert.AreEqual(1, PresentMonInterface.ClampCaptureSeconds(-5), "negative -> 1");
            Assert.AreEqual(20, PresentMonInterface.ClampCaptureSeconds(20), "in-range unchanged");
            Assert.AreEqual(300, PresentMonInterface.ClampCaptureSeconds(999999), "cap the DoS value");
            Assert.AreEqual(300, PresentMonInterface.ClampCaptureSeconds(2000000000), "cap the overflow value");
        }
    }
}
