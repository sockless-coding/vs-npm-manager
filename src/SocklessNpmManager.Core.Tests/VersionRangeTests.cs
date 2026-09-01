using SocklessNpmManager.Core.Model;
using SocklessNpmManager.Core.Npm;
using Xunit;

namespace SocklessNpmManager.Core.Tests
{
    public class VersionRangeTests
    {
        [Fact]
        public void RecognisesExactVersionPins()
        {
            Assert.True(VersionRange.IsExactVersionPin("1.2.3"));
            Assert.True(VersionRange.IsExactVersionPin("  1.2.3 "));
            Assert.True(VersionRange.IsExactVersionPin("1.2.3-beta.1"));
            Assert.False(VersionRange.IsExactVersionPin("^1.2.3"));
            Assert.False(VersionRange.IsExactVersionPin("~1.2.3"));
            Assert.False(VersionRange.IsExactVersionPin(">=1.2.3 <2.0.0"));
            Assert.False(VersionRange.IsExactVersionPin("1.2.x"));
            Assert.False(VersionRange.IsExactVersionPin("*"));
            Assert.False(VersionRange.IsExactVersionPin(""));
            Assert.False(VersionRange.IsExactVersionPin(null));
        }

        [Fact]
        public void StripVersionPinExtractsTheBaseVersion()
        {
            Assert.Equal("1.2.3", VersionRange.StripVersionPin("1.2.3"));
            Assert.Equal("1.2.3", VersionRange.StripVersionPin("^1.2.3"));
            Assert.Equal("1.2.3", VersionRange.StripVersionPin("~1.2.3"));
            Assert.Equal("1.2.3", VersionRange.StripVersionPin(" 1.2.3 "));
            Assert.Equal("1.2.3", VersionRange.StripVersionPin(">=1.2.3 <2.0.0"));
            Assert.Equal("", VersionRange.StripVersionPin(null));
        }

        [Fact]
        public void ToExactVersionPinStripsAnyRangePrefix()
        {
            Assert.Equal("1.2.3", VersionRange.ToExactVersionPin("^1.2.3"));
            Assert.Equal("1.2.3", VersionRange.ToExactVersionPin("1.2.3"));
        }

        [Fact]
        public void ToCaretRangeWrapsAPlainVersion()
        {
            Assert.Equal("^1.2.3", VersionRange.ToCaretRange("1.2.3"));
            Assert.Equal("^1.2.3", VersionRange.ToCaretRange("^1.2.3"));
        }

        [Fact]
        public void ApplyVersionPrefixWritesEachSelector()
        {
            Assert.Equal("1.2.3", VersionRange.ApplyVersionPrefix("1.2.3", VersionPrefix.Exact));
            Assert.Equal("^1.2.3", VersionRange.ApplyVersionPrefix("1.2.3", VersionPrefix.Caret));
            Assert.Equal("~1.2.3", VersionRange.ApplyVersionPrefix("1.2.3", VersionPrefix.Tilde));
            Assert.Equal(">=1.2.3", VersionRange.ApplyVersionPrefix("1.2.3", VersionPrefix.Gte));
            Assert.Equal("~1.2.3", VersionRange.ApplyVersionPrefix("^1.2.3", VersionPrefix.Tilde));
        }

        [Fact]
        public void DetectVersionPrefixRecognisesEachSelectorDefaultingToCaret()
        {
            Assert.Equal(VersionPrefix.Caret, VersionRange.DetectVersionPrefix("^1.2.3"));
            Assert.Equal(VersionPrefix.Tilde, VersionRange.DetectVersionPrefix("~1.2.3"));
            Assert.Equal(VersionPrefix.Gte, VersionRange.DetectVersionPrefix(">=1.2.3"));
            Assert.Equal(VersionPrefix.Exact, VersionRange.DetectVersionPrefix("1.2.3"));
            Assert.Equal(VersionPrefix.Caret, VersionRange.DetectVersionPrefix("1.2.x"));
            Assert.Equal(VersionPrefix.Caret, VersionRange.DetectVersionPrefix("workspace:*"));
            Assert.Equal(VersionPrefix.Caret, VersionRange.DetectVersionPrefix(null));
        }
    }
}
