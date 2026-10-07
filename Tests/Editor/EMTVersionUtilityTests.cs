using NUnit.Framework;

namespace EMT.Packages.Editor.Tests
{
    public class EMTVersionUtilityTests
    {
        [TestCase("1.2.3")]
        [TestCase("v1.2.3")]
        [TestCase("0.0.0")]
        [TestCase("1.2.3-beta.1")]
        [TestCase("1.2.3-rc.1+build.5")]
        public void TryParse_Valid(string text) => Assert.IsTrue(EMTVersionUtility.TryParse(text, out _));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("1.2")]
        [TestCase("1.2.3.4")]
        [TestCase("01.2.3")]
        [TestCase("a.b.c")]
        [TestCase("1.2.x")]
        [TestCase("99999999999.0.0")]
        public void TryParse_Invalid(string text) => Assert.IsFalse(EMTVersionUtility.TryParse(text, out _));

        [Test]
        public void Compare_IsNumeric_NotLexicographic()
        {
            Assert.IsTrue(EMTVersionUtility.TryCompare("1.10.0", "1.9.0", out int c));
            Assert.Greater(c, 0);
        }

        [TestCase("1.0.0", "1.0.0", 0)]
        [TestCase("1.0.0", "1.0.1", -1)]
        [TestCase("1.1.0", "1.0.9", 1)]
        [TestCase("2.0.0", "1.99.99", 1)]
        [TestCase("v1.2.0", "1.2.0", 0)]
        [TestCase("1.0.0+a", "1.0.0+b", 0)]
        [TestCase("1.0.0-alpha", "1.0.0", -1)]
        [TestCase("1.0.0-alpha", "1.0.0-alpha.1", -1)]
        [TestCase("1.0.0-alpha.1", "1.0.0-alpha.beta", -1)]
        [TestCase("1.0.0-alpha.beta", "1.0.0-beta", -1)]
        [TestCase("1.0.0-beta.2", "1.0.0-beta.11", -1)]
        [TestCase("1.0.0-beta.11", "1.0.0-rc.1", -1)]
        public void Compare_Table(string a, string b, int expected)
        {
            Assert.IsTrue(EMTVersionUtility.TryCompare(a, b, out int c));
            Assert.AreEqual(expected, c);
        }

        [Test]
        public void ToString_RoundTrips()
        {
            EMTVersionUtility.TryParse("v1.2.3-rc.1+b5", out EMTSemVer v);
            Assert.AreEqual("1.2.3-rc.1+b5", v.ToString());
        }
    }
}
