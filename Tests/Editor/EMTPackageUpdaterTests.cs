using NUnit.Framework;

namespace EMT.Packages.Editor.Tests
{
    public class EMTPackageUpdaterTests
    {
        private const string Manifest = @"{
  ""dependencies"": {
    ""com.emt.core"": ""https://github.com/DonMario-Git/EMTCore.git#v1.2.0"",
    ""com.emt.ui"": ""https://github.com/DonMario-Git/EMTUI.git?path=/Package#v1.1.0"",
    ""com.emt.local"": ""file:../../EMTLocal"",
    ""com.emt.reg"": ""1.0.0"",
    ""com.unity.ugui"": ""2.0.0""
  },
  ""scopedRegistries"": [
    { ""name"": ""x"", ""url"": ""https://x.example"", ""scopes"": [ ""com.emt.core"" ] }
  ]
}";

        private const string CoreRepo = "https://github.com/DonMario-Git/EMTCore.git";

        [Test]
        public void ReplacesOnlyTheRef_AndKeepsEverythingElse()
        {
            Assert.IsTrue(EMTPackageUpdater.TryReplaceDependency(Manifest, "com.emt.core", "v1.3.0", CoreRepo, out string json, out string error), error);
            Assert.AreEqual(Manifest.Replace("EMTCore.git#v1.2.0", "EMTCore.git#v1.3.0"), json);
        }

        [Test]
        public void KeepsPathQuery()
        {
            Assert.IsTrue(EMTPackageUpdater.TryReplaceDependency(Manifest, "com.emt.ui", "v2.0.0", null, out string json, out _));
            StringAssert.Contains("EMTUI.git?path=/Package#v2.0.0", json);
        }

        [Test]
        public void RefusesLocalPackages()
        {
            Assert.IsFalse(EMTPackageUpdater.TryReplaceDependency(Manifest, "com.emt.local", "v1.0.0", null, out string json, out string error));
            Assert.AreEqual(Manifest, json);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void RefusesRegistryPackages() =>
            Assert.IsFalse(EMTPackageUpdater.TryReplaceDependency(Manifest, "com.emt.reg", "v1.1.0", null, out _, out _));

        [Test]
        public void RefusesMissingPackage() =>
            Assert.IsFalse(EMTPackageUpdater.TryReplaceDependency(Manifest, "com.emt.missing", "v1.0.0", null, out _, out _));

        [Test]
        public void RefusesNonEMTPackage() =>
            Assert.IsFalse(EMTPackageUpdater.TryReplaceDependency(Manifest, "com.unity.ugui", "v3.0.0", null, out _, out _));

        [TestCase("")]
        [TestCase("latest")]
        [TestCase("v1.0.0\",\"evil\":\"x")]
        public void RefusesUnvalidatedTag(string tag) =>
            Assert.IsFalse(EMTPackageUpdater.TryReplaceDependency(Manifest, "com.emt.core", tag, null, out _, out _));

        [Test]
        public void RefusesWhenAlreadyOnThatTag() =>
            Assert.IsFalse(EMTPackageUpdater.TryReplaceDependency(Manifest, "com.emt.core", "v1.2.0", null, out _, out _));

        [Test]
        public void RefusesRepositoryMismatch() =>
            Assert.IsFalse(EMTPackageUpdater.TryReplaceDependency(Manifest, "com.emt.core", "v1.3.0",
                "https://github.com/someone-else/Fork.git", out _, out _));

        [Test]
        public void CanUpdate_RequiresGitAndValidatedUpdate()
        {
            var ok = new EMTPackageInfo
            {
                Name = "com.emt.core", Source = EMTPackageSource.Git,
                Status = EMTUpdateStatus.UpdateAvailable, LatestTag = "v1.3.0"
            };
            Assert.IsTrue(EMTPackageUpdater.CanUpdate(ok, out _));

            ok.Source = EMTPackageSource.Local;
            Assert.IsFalse(EMTPackageUpdater.CanUpdate(ok, out _));

            ok.Source = EMTPackageSource.Git;
            ok.Status = EMTUpdateStatus.UpToDate;
            Assert.IsFalse(EMTPackageUpdater.CanUpdate(ok, out _));

            ok.Status = EMTUpdateStatus.UpdateAvailable;
            ok.LatestTag = null;
            Assert.IsFalse(EMTPackageUpdater.CanUpdate(ok, out _));
        }
    }
}
