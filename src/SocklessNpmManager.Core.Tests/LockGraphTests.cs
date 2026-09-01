using Newtonsoft.Json.Linq;
using SocklessNpmManager.Core.Projects;
using Xunit;

namespace SocklessNpmManager.Core.Tests
{
    public class LockGraphTests
    {
        private const string LockfileV3 = @"{
  ""name"": ""demo"",
  ""lockfileVersion"": 3,
  ""packages"": {
    """": { ""name"": ""demo"", ""dependencies"": { ""express"": ""^4.18.0"" } },
    ""node_modules/express"": { ""version"": ""4.18.2"", ""dependencies"": { ""body-parser"": ""1.20.1"" } },
    ""node_modules/body-parser"": { ""version"": ""1.20.1"" },
    ""node_modules/@scope/util"": { ""version"": ""2.1.0"" }
  }
}";

        [Fact]
        public void BuildGraphFromNpmLockfileResolvesVersionsAndEdges()
        {
            var graph = LockGraph.BuildGraphFromNpmLockfile(JObject.Parse(LockfileV3));

            Assert.Equal("4.18.2", graph.Resolved["express"]);
            Assert.Equal("1.20.1", graph.Resolved["body-parser"]);
            Assert.Equal("2.1.0", graph.Resolved["@scope/util"]);

            Assert.Contains("body-parser", graph.Dependencies["express"]);
            Assert.Contains("express", graph.Dependents["body-parser"]);
        }

        [Fact]
        public void MergeGraphsUnionsEdgesAndKeepsFirstResolvedVersion()
        {
            var a = LockGraph.BuildGraphFromNpmLockfile(JObject.Parse(LockfileV3));
            var b = LockGraph.BuildGraphFromNpmLockfile(JObject.Parse(@"{
  ""packages"": {
    ""node_modules/express"": { ""version"": ""9.9.9"" },
    ""node_modules/left-pad"": { ""version"": ""1.3.0"" }
  }
}"));

            var merged = LockGraph.MergeGraphs(new[] { a, b });

            Assert.Equal("4.18.2", merged.Resolved["express"]); // first wins
            Assert.Equal("1.3.0", merged.Resolved["left-pad"]);
        }

        [Fact]
        public void BuildGraphFromNpmLockfileHandlesMissingPackagesMap()
        {
            var graph = LockGraph.BuildGraphFromNpmLockfile(JObject.Parse(@"{ ""lockfileVersion"": 1 }"));
            Assert.Empty(graph.Resolved);
        }
    }
}
