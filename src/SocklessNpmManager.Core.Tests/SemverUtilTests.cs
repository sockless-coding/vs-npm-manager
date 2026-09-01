using SocklessNpmManager.Core.Npm;
using Xunit;

namespace SocklessNpmManager.Core.Tests
{
    public class SemverUtilTests
    {
        [Fact]
        public void IsPrereleaseDetectsPreReleaseTags()
        {
            Assert.True(SemverUtil.IsPrerelease("2.0.0-beta.1"));
            Assert.False(SemverUtil.IsPrerelease("2.0.0"));
            Assert.False(SemverUtil.IsPrerelease("not-a-version"));
        }

        [Fact]
        public void SortVersionsDescendingPutsNewestFirstAndUnparseableLast()
        {
            var sorted = SemverUtil.SortVersionsDescending(new[] { "1.0.0", "2.0.0", "1.5.0", "garbage", "2.0.0-rc.1" });
            Assert.Equal(new[] { "2.0.0", "2.0.0-rc.1", "1.5.0", "1.0.0", "garbage" }, sorted);
        }

        [Fact]
        public void MaxVersionRespectsThePrereleaseFilter()
        {
            var versions = new[] { "1.0.0", "2.0.0-beta", "1.9.0" };
            Assert.Equal("1.9.0", SemverUtil.MaxVersion(versions, includePrerelease: false));
            Assert.Equal("2.0.0-beta", SemverUtil.MaxVersion(versions, includePrerelease: true));
        }
    }
}
