using NUnit.Framework;

namespace EMT.Packages.Editor.Tests
{
    public class EMTPackageVersionCheckerTests
    {
        [TestCase("1.2.0", "1.2.0", EMTUpdateStatus.UpToDate)]
        [TestCase("1.2.0", "1.3.0", EMTUpdateStatus.UpdateAvailable)]
        [TestCase("1.2.0", "1.2.1", EMTUpdateStatus.UpdateAvailable)]
        [TestCase("1.9.0", "1.10.0", EMTUpdateStatus.UpdateAvailable)]
        [TestCase("1.2.0", "2.0.0", EMTUpdateStatus.MajorUpdateAvailable)]
        [TestCase("1.3.0", "1.2.0", EMTUpdateStatus.UpToDate)]        // installed ahead of latest
        [TestCase("abc", "1.2.0", EMTUpdateStatus.InvalidVersion)]
        [TestCase("1.2.0", "latest", EMTUpdateStatus.InvalidVersion)]
        [TestCase(null, "1.2.0", EMTUpdateStatus.InvalidVersion)]
        public void Evaluate(string installed, string latest, EMTUpdateStatus expected) =>
            Assert.AreEqual(expected, EMTPackageVersionChecker.Evaluate(installed, latest));
    }
}
