using NUnit.Framework;

namespace EMT.Packages.Editor.Tests
{
    public class EMTPackageRegistryTests
    {
        [Test]
        public void Parse_ValidTemplate()
        {
            var list = EMTPackageRegistry.Parse(EMTPackageRegistry.Template, out string error);
            Assert.IsNull(error);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("com.emt.core", list[0].Name);
            Assert.AreEqual(EMTRegistryState.NotChecked, list[0].State);
        }

        [Test]
        public void Parse_MarksInvalidEntries()
        {
            const string json = @"{ ""packages"": [
              { ""name"": ""com.unity.emt"", ""repository"": ""https://github.com/a/b.git"" },
              { ""name"": ""com.emt.ui"",    ""repository"": ""https://gitlab.com/a/b.git"" },
              { ""name"": ""com.emt.ok"",    ""repository"": ""https://github.com/a/ok.git"" },
              { ""name"": ""com.emt.ok"",    ""repository"": ""https://github.com/a/ok.git"" }
            ] }";
            var list = EMTPackageRegistry.Parse(json, out _);
            Assert.AreEqual(4, list.Count);
            Assert.AreEqual(EMTRegistryState.Invalid, list[0].State); // wrong prefix
            Assert.AreEqual(EMTRegistryState.Invalid, list[1].State); // not GitHub
            Assert.AreEqual(EMTRegistryState.NotChecked, list[2].State);
            Assert.AreEqual(EMTRegistryState.Invalid, list[3].State); // duplicate
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("{}")]
        public void Parse_BadJson_ReportsErrorWithoutThrowing(string json)
        {
            var list = EMTPackageRegistry.Parse(json, out string error);
            Assert.IsEmpty(list);
            Assert.IsNotEmpty(error);
        }
    }
}
