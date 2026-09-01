using Newtonsoft.Json.Linq;
using SocklessNpmManager.Core.Model;
using SocklessNpmManager.Core.Projects;
using Xunit;

namespace SocklessNpmManager.Core.Tests
{
    public class PackageJsonEditorTests
    {
        private const string Pkg =
            "{\n" +
            "  \"name\": \"demo\",\n" +
            "  \"version\": \"1.0.0\",\n" +
            "  \"dependencies\": {\n" +
            "    \"lodash\": \"^4.17.21\"\n" +
            "  },\n" +
            "  \"devDependencies\": {\n" +
            "    \"typescript\": \"^5.5.3\"\n" +
            "  }\n" +
            "}\n";

        [Fact]
        public void UpsertUpdatesAnExistingEntryInPlace()
        {
            var updated = PackageJsonEditor.UpsertDependency(Pkg, "lodash", "^4.17.22", DependencyType.Dependencies);
            var doc = JObject.Parse(updated);
            Assert.Equal("^4.17.22", (string?)doc["dependencies"]!["lodash"]);
        }

        [Fact]
        public void UpsertInsertsANewKeyAlphabetically()
        {
            var updated = PackageJsonEditor.UpsertDependency(Pkg, "axios", "^1.7.0", DependencyType.Dependencies);
            var doc = JObject.Parse(updated);
            var keys = ((JObject)doc["dependencies"]!).Properties();
            Assert.Equal(new[] { "axios", "lodash" }, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(keys, p => p.Name)));
        }

        [Fact]
        public void UpsertCreatesAMissingSection()
        {
            var updated = PackageJsonEditor.UpsertDependency(Pkg, "react", "^18.3.1", DependencyType.PeerDependencies);
            var doc = JObject.Parse(updated);
            Assert.Equal("^18.3.1", (string?)doc["peerDependencies"]!["react"]);
        }

        [Fact]
        public void RemoveDeletesTheKeyAndDropsAnEmptiedSection()
        {
            var onlyDep = "{\n  \"name\": \"demo\",\n  \"dependencies\": {\n    \"lodash\": \"^4.17.21\"\n  }\n}\n";
            var updated = PackageJsonEditor.RemoveDependency(onlyDep, "lodash");
            var doc = JObject.Parse(updated);
            Assert.False(doc.ContainsKey("dependencies"));
        }

        [Fact]
        public void RemoveIsANoOpWhenThePackageIsAbsent()
        {
            Assert.Equal(Pkg, PackageJsonEditor.RemoveDependency(Pkg, "not-there"));
        }

        [Fact]
        public void PreservesIndentationAndTrailingNewline()
        {
            var tabIndented = "{\n\t\"name\": \"demo\",\n\t\"dependencies\": {\n\t\t\"lodash\": \"^4.17.21\"\n\t}\n}\n";
            var updated = PackageJsonEditor.UpsertDependency(tabIndented, "lodash", "^4.17.22", DependencyType.Dependencies);
            Assert.Contains("\t\"dependencies\": {", updated);
            Assert.EndsWith("\n", updated);
        }
    }
}
