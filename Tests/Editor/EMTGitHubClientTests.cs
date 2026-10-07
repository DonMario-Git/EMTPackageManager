using NUnit.Framework;

namespace EMT.Packages.Editor.Tests
{
    public class EMTGitHubClientTests
    {
        [TestCase("https://github.com/DonMario-Git/EMTCore.git", "DonMario-Git", "EMTCore")]
        [TestCase("https://github.com/DonMario-Git/EMTCore", "DonMario-Git", "EMTCore")]
        [TestCase("https://github.com/DonMario-Git/EMTCore/", "DonMario-Git", "EMTCore")]
        [TestCase("https://github.com/DonMario-Git/EMTCore.git#v1.2.0", "DonMario-Git", "EMTCore")]
        [TestCase("https://github.com/DonMario-Git/EMTCore.git?path=/pkg#v1.2.0", "DonMario-Git", "EMTCore")]
        [TestCase("git+https://github.com/DonMario-Git/EMTCore.git", "DonMario-Git", "EMTCore")]
        [TestCase("git@github.com:DonMario-Git/EMTCore.git", "DonMario-Git", "EMTCore")]
        [TestCase("ssh://git@github.com/DonMario-Git/EMTCore.git", "DonMario-Git", "EMTCore")]
        public void TryParseRepository_Valid(string url, string owner, string repo)
        {
            Assert.IsTrue(EMTGitHubClient.TryParseRepository(url, out string o, out string r));
            Assert.AreEqual(owner, o);
            Assert.AreEqual(repo, r);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not a url")]
        [TestCase("https://gitlab.com/a/b.git")]
        [TestCase("https://github.com/onlyowner")]
        [TestCase("https://github.com/a/b/c")]
        [TestCase("file:../../EMTCore")]
        public void TryParseRepository_Invalid(string url) =>
            Assert.IsFalse(EMTGitHubClient.TryParseRepository(url, out _, out _));

        [Test]
        public void ParseLatestRelease_Valid()
        {
            EMTReleaseResult r = EMTGitHubClient.ParseLatestRelease(
                "{\"tag_name\":\"v1.3.0\",\"html_url\":\"https://github.com/a/b/releases/tag/v1.3.0\",\"draft\":false}");
            Assert.IsTrue(r.Success);
            Assert.AreEqual("v1.3.0", r.TagName);
            Assert.AreEqual("https://github.com/a/b/releases/tag/v1.3.0", r.ReleaseUrl);
        }

        [Test]
        public void ParseLatestRelease_DropsNonGitHubUrl()
        {
            EMTReleaseResult r = EMTGitHubClient.ParseLatestRelease("{\"tag_name\":\"1.0.0\",\"html_url\":\"https://evil.example/x\"}");
            Assert.IsTrue(r.Success);
            Assert.IsNull(r.ReleaseUrl);
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("{}")]
        [TestCase("{\"tag_name\":\"\"}")]
        public void ParseLatestRelease_InvalidResponse(string json)
        {
            EMTReleaseResult r = EMTGitHubClient.ParseLatestRelease(json);
            Assert.IsFalse(r.Success);
            Assert.AreEqual(EMTGitHubError.InvalidResponse, r.Error);
        }

        [Test]
        public void ParseLatestRelease_InvalidVersionTag()
        {
            EMTReleaseResult r = EMTGitHubClient.ParseLatestRelease("{\"tag_name\":\"release-final\"}");
            Assert.IsFalse(r.Success);
            Assert.AreEqual(EMTGitHubError.InvalidVersion, r.Error);
        }
    }
}
