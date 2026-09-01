using System;
using System.Collections.Generic;
using System.Linq;
using SocklessNpmManager.Core.Model;
using SocklessNpmManager.Core.Npm;
using Xunit;

namespace SocklessNpmManager.Core.Tests
{
    public class PackageAgeTests
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-30T00:00:00Z");

        private static string DaysAgo(double n) => Now.AddDays(-n).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        [Fact]
        public void AgeInDaysReturnsInfinityForMissingOrUnparseableDates()
        {
            Assert.Equal(double.PositiveInfinity, PackageAge.AgeInDays(null, Now));
            Assert.Equal(double.PositiveInfinity, PackageAge.AgeInDays("not-a-date", Now));
        }

        [Fact]
        public void AgeInDaysMeasuresElapsedDays()
        {
            Assert.Equal(10, Math.Round(PackageAge.AgeInDays(DaysAgo(10), Now)));
        }

        [Fact]
        public void FormatRelativeAgeProducesHumanStrings()
        {
            Assert.Equal("just now", PackageAge.FormatRelativeAge(DaysAgo(0), Now));
            Assert.Equal("1 day ago", PackageAge.FormatRelativeAge(DaysAgo(1), Now));
            Assert.Equal("3 days ago", PackageAge.FormatRelativeAge(DaysAgo(3), Now));
            Assert.Equal("2 months ago", PackageAge.FormatRelativeAge(DaysAgo(60), Now));
            Assert.Equal("", PackageAge.FormatRelativeAge(null, Now));
        }

        private static IReadOnlyList<VersionInfo> Versions(params (string version, double age, bool pre)[] specs) =>
            specs.Select(s => new VersionInfo { Version = s.version, IsPrerelease = s.pre, Published = DaysAgo(s.age) }).ToList();

        [Fact]
        public void PickDefaultVersionHoldsBackVersionsNewerThanTheMinimumAge()
        {
            var v = Versions(("13.0.4", 2, false), ("13.0.3", 40, false), ("13.0.1", 400, false));
            Assert.Equal("13.0.3", PackageAge.PickDefaultVersion(v, false, 7, Now));
        }

        [Fact]
        public void PickDefaultVersionFallsBackToNewestWhenAllAreTooNew()
        {
            var v = Versions(("2.0.0", 1, false), ("1.9.0", 3, false));
            Assert.Equal("2.0.0", PackageAge.PickDefaultVersion(v, false, 7, Now));
        }

        [Fact]
        public void PickDefaultVersionReturnsNewestWhenTheCheckIsDisabled()
        {
            var v = Versions(("2.0.0", 1, false), ("1.9.0", 300, false));
            Assert.Equal("2.0.0", PackageAge.PickDefaultVersion(v, false, 0, Now));
        }

        [Fact]
        public void PickDefaultVersionRespectsThePrereleaseFilter()
        {
            var v = Versions(("2.0.0-beta", 90, true), ("1.9.0", 90, false));
            Assert.Equal("1.9.0", PackageAge.PickDefaultVersion(v, false, 7, Now));
            Assert.Equal("2.0.0-beta", PackageAge.PickDefaultVersion(v, true, 7, Now));
        }
    }
}
