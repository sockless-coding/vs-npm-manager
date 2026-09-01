using SocklessNpmManager.Core.Npm;
using Xunit;

namespace SocklessNpmManager.Core.Tests
{
    public class NpmrcParserTests
    {
        [Fact]
        public void ParsesTheDefaultRegistryAndScopedRegistries()
        {
            var p = NpmrcParser.Parse("\nregistry=https://registry.npmjs.org/\n@myco:registry=https://npm.myco.dev/\n");
            Assert.Equal("https://registry.npmjs.org/", p.Registry);
            Assert.Equal("https://npm.myco.dev/", p.ScopedRegistries["myco"]);
        }

        [Fact]
        public void ParsesHostScopedAuthEntries()
        {
            var p = NpmrcParser.Parse("//npm.myco.dev/:_authToken=abc123\n//legacy.myco.dev/:_auth=dXNlcjpwYXNz\n");
            Assert.Equal("abc123", p.AuthTokens["npm.myco.dev/"]);
            Assert.Equal("dXNlcjpwYXNz", p.BasicAuth["legacy.myco.dev/"]);
        }

        [Fact]
        public void IgnoresCommentsAndBlankLines()
        {
            var p = NpmrcParser.Parse("# a comment\n; also a comment\n\nregistry=https://registry.npmjs.org/\n");
            Assert.Equal("https://registry.npmjs.org/", p.Registry);
        }

        [Fact]
        public void MergeNearestConfigWinsOnConflicts()
        {
            var merged = NpmrcParser.Merge(new[]
            {
                NpmrcParser.Parse("registry=https://global/\n"),
                NpmrcParser.Parse("registry=https://project/\n@myco:registry=https://npm.myco.dev/\n"),
            });
            Assert.Equal("https://project/", merged.Registry);
            Assert.Equal("https://npm.myco.dev/", merged.ScopedRegistries["myco"]);
        }

        [Fact]
        public void FindAuthPrefixMatchesByHostPathPrefix()
        {
            var keys = new[] { "npm.myco.dev/packages" };
            Assert.Equal("npm.myco.dev/packages", NpmrcParser.FindAuthPrefix(keys, "https://npm.myco.dev/packages/some-lib"));
            Assert.Null(NpmrcParser.FindAuthPrefix(keys, "https://other.dev/packages/some-lib"));
        }
    }
}
