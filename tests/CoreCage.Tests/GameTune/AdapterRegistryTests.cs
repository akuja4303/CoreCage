using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.Core.GameTune;

namespace CoreCage.Tests.GameTune
{
    [TestClass]
    public class AdapterRegistryTests
    {
        [TestMethod]
        public void For_KnownFormats_ReturnMatchingAdapter()
        {
            Assert.AreEqual("unreal-ini", AdapterRegistry.For("unreal-ini").Format);
            Assert.AreEqual("frostbite-profsave", AdapterRegistry.For("frostbite-profsave").Format);
            Assert.AreEqual("stingray-config", AdapterRegistry.For("stingray-config").Format);
            Assert.AreEqual("source-cfg", AdapterRegistry.For("source-cfg").Format);
        }

        [TestMethod]
        [ExpectedException(typeof(NotSupportedException))]
        public void For_UnknownFormat_Throws()
        {
            AdapterRegistry.For("does-not-exist");
        }
    }
}
