using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoreCage.Core.Modes;

namespace CoreCage.Tests
{
    /// <summary>
    /// TDD coverage for the IModeModule seam: ModeRegistry's built-in list + Register/Get, and the
    /// IModeModule contract itself via a test-only FakeModeModule. GamingMode.ApplyAsync/RevertAsync
    /// are NOT invoked here -- they mutate the live rig (MSI mode, NIC props, power plan, IFEO...).
    /// Their pipeline correctness is verified live in Task 11.
    /// </summary>
    [TestClass]
    public class ModeRegistryTests
    {
        [TestMethod]
        public void Modules_ContainsExactlyOneBuiltIn_NamedGaming()
        {
            var gamingModules = ModeRegistry.Modules.Where(m => m.Name == "Gaming").ToList();
            Assert.AreEqual(1, gamingModules.Count, "expected exactly one built-in module named 'Gaming'");
            Assert.IsInstanceOfType(gamingModules[0], typeof(GamingMode));
        }

        [TestMethod]
        public void Get_ReturnsGaming_ByName_AndNullForUnknownName()
        {
            var gaming = ModeRegistry.Get("Gaming");
            Assert.IsNotNull(gaming);
            Assert.AreEqual("Gaming", gaming!.Name);

            Assert.IsNull(ModeRegistry.Get("Trading"));
        }

        [TestMethod]
        public void Register_AddsModule_RetrievableByGet()
        {
            var fake = new FakeModeModule("FakeRegisterTest-" + Guid.NewGuid());
            ModeRegistry.Register(fake);

            var retrieved = ModeRegistry.Get(fake.Name);
            Assert.AreSame(fake, retrieved);
        }

        [TestMethod]
        public async Task FakeModeModule_RoundTrips_ApplyThenRevert_TogglingIsActive()
        {
            var fake = new FakeModeModule("FakeRoundTrip-" + Guid.NewGuid());
            Assert.IsFalse(fake.IsActive, "fake should start inactive");

            ModeResult applyResult = await fake.ApplyAsync();
            Assert.IsTrue(applyResult.Success, "ApplyAsync should report success");
            Assert.IsTrue(fake.IsActive, "IsActive should be true after ApplyAsync");
            Assert.IsTrue(applyResult.Steps.Count > 0, "ApplyAsync should report at least one step");

            ModeResult revertResult = await fake.RevertAsync();
            Assert.IsTrue(revertResult.Success, "RevertAsync should report success");
            Assert.IsFalse(fake.IsActive, "IsActive should be false after RevertAsync");
            Assert.IsTrue(revertResult.Steps.Count > 0, "RevertAsync should report at least one step");
        }

        /// <summary>
        /// Test-only IModeModule implementation. Proves the ModeRegistry seam is usable by a module
        /// defined entirely outside CoreCage.Core -- exactly the shape future private modules will use.
        /// Never touches the OS.
        /// </summary>
        private sealed class FakeModeModule : IModeModule
        {
            public FakeModeModule(string name) => Name = name;

            public string Name { get; }
            public string Description => "Fake in-memory mode module for tests.";
            public bool IsActive { get; private set; }

            public Task<ModeResult> ApplyAsync(IProgress<string>? progress = null)
            {
                progress?.Report("fake: applying");
                IsActive = true;
                return Task.FromResult(new ModeResult(true, "fake applied", new List<string> { "applied" }));
            }

            public Task<ModeResult> RevertAsync(IProgress<string>? progress = null)
            {
                progress?.Report("fake: reverting");
                IsActive = false;
                return Task.FromResult(new ModeResult(true, "fake reverted", new List<string> { "reverted" }));
            }
        }
    }
}
