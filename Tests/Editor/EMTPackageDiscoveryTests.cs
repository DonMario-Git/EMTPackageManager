using NUnit.Framework;

namespace EMT.Packages.Editor.Tests
{
    public class EMTPackageDiscoveryTests
    {
        [TestCase("com.emt.core", true)]
        [TestCase("com.emt.ui", true)]
        [TestCase("com.emt.firebase", true)]
        [TestCase("com.emt.nuevo-paquete", true)]
        [TestCase("com.emt.localization", true)]
        [TestCase("com.emt", false)]
        [TestCase("com.emt.", false)]
        [TestCase("com.unity.emt", false)]
        [TestCase("com.unity.test", false)]
        [TestCase("emt.core", false)]
        [TestCase("com.company.core", false)]
        [TestCase("com.company.emt.core", false)]
        [TestCase("core", false)]
        [TestCase("COM.EMT.CORE", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsEMTPackage(string name, bool expected) =>
            Assert.AreEqual(expected, EMTPackageDiscovery.IsEMTPackage(name));
    }
}
