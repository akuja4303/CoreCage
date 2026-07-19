using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core;

namespace CoreCage.Tests
{
    /// <summary>
    /// Covers the app-exit restore-honesty path: a restore step that throws on shutdown must be captured
    /// and written to last-exit-errors.txt — NOT swallowed by the logger teardown. This is the test the
    /// completeness council required to call the restore-honesty theme done.
    /// </summary>
    [TestClass]
    public class ExitRestoreTests
    {
        [TestMethod]
        public void RunRestoreSteps_CapturesThrowingStep_WithLabel_AndSkipsHealthyOnes()
        {
            bool healthyRan = false;
            var failures = ExitRestore.RunRestoreSteps(new (string, Action)[]
            {
                ("HealthyStep", () => healthyRan = true),
                ("RestorePowerPlan", () => throw new InvalidOperationException("powercfg blew up")),
            });

            Assert.IsTrue(healthyRan, "healthy step must still run");
            Assert.AreEqual(1, failures.Count, "only the throwing step is a failure");
            StringAssert.Contains(failures[0], "RestorePowerPlan", "failure must carry the step label");
            StringAssert.Contains(failures[0], "powercfg blew up", "failure must carry the exception message");
            Assert.IsFalse(failures.Exists(f => f.Contains("HealthyStep")), "healthy step must not appear as a failure");
        }

        [TestMethod]
        public void RunRestoreSteps_AllClean_ReturnsEmpty()
        {
            var failures = ExitRestore.RunRestoreSteps(new (string, Action)[]
            {
                ("A", () => { }),
                ("B", () => { }),
            });
            Assert.AreEqual(0, failures.Count);
        }

        [TestMethod]
        public void WriteExitErrors_WritesFile_ContainingEveryFailure_WhenThereAreFailures()
        {
            string dir = Path.Combine(Path.GetTempPath(), "CoreCageExitTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                var failures = new List<string> { "RestorePowerPlan: powercfg blew up", "StopWatching: WMI gone" };
                bool wrote = ExitRestore.WriteExitErrors(failures, dir);

                Assert.IsTrue(wrote, "should report it wrote the file");
                string path = Path.Combine(dir, "last-exit-errors.txt");
                Assert.IsTrue(File.Exists(path), "last-exit-errors.txt must exist");
                string content = File.ReadAllText(path);
                StringAssert.Contains(content, "RestorePowerPlan: powercfg blew up");
                StringAssert.Contains(content, "StopWatching: WMI gone");
                StringAssert.Contains(content, "2 restore step(s) failed");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [TestMethod]
        public void WriteExitErrors_IsNoOp_WhenNoFailures()
        {
            string dir = Path.Combine(Path.GetTempPath(), "CoreCageExitTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.IsFalse(ExitRestore.WriteExitErrors(new List<string>(), dir), "empty list → no file, returns false");
                Assert.IsFalse(File.Exists(Path.Combine(dir, "last-exit-errors.txt")), "no file should be created");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
