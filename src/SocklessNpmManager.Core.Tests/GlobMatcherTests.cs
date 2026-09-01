using SocklessNpmManager.Core.Projects;
using Xunit;

namespace SocklessNpmManager.Core.Tests
{
    public class GlobMatcherTests
    {
        [Theory]
        [InlineData("packages/*/package.json", "packages/app/package.json", true)]
        [InlineData("packages/*/package.json", "packages/app/nested/package.json", false)]
        [InlineData("packages/**/package.json", "packages/app/nested/package.json", true)]
        [InlineData("packages/**/package.json", "packages/package.json", true)]
        [InlineData("apps/*/package.json", "libs/app/package.json", false)]
        [InlineData("packages/*/package.json", "packages/App/package.json", true)] // case-insensitive
        public void MatchesWorkspacePatterns(string pattern, string path, bool expected)
        {
            Assert.Equal(expected, GlobMatcher.IsMatch(pattern, path));
        }
    }
}
