using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SocklessNpmManager.Core.Model;

namespace SocklessNpmManager.Core.Npm
{
    /// <summary>
    /// Package-age helpers for the supply-chain guardrail. Port of <c>src/webview/packageAge.ts</c>.
    /// </summary>
    public static class PackageAge
    {
        private static long NowMs(DateTimeOffset? now) => (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();

        private static bool TryParseMs(string? iso, out long ms)
        {
            ms = 0;
            if (string.IsNullOrWhiteSpace(iso)) return false;
            if (DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
            {
                ms = dto.ToUnixTimeMilliseconds();
                return true;
            }

            return false;
        }

        private const double DayMs = 24 * 60 * 60 * 1000;

        /// <summary>Whole/fractional days since <paramref name="iso"/>; <see cref="double.PositiveInfinity"/> when missing or unparseable.</summary>
        public static double AgeInDays(string? iso, DateTimeOffset? now = null)
        {
            if (!TryParseMs(iso, out var t)) return double.PositiveInfinity;
            return Math.Max(0, (NowMs(now) - t) / DayMs);
        }

        /// <summary>"just now" / "3 days ago" / "2 months ago"; "" when the date is unknown.</summary>
        public static string FormatRelativeAge(string? iso, DateTimeOffset? now = null)
        {
            if (!TryParseMs(iso, out var t)) return "";
            var deltaMs = NowMs(now) - t;
            var days = (long)Math.Floor(deltaMs / DayMs);
            if (days <= 0)
            {
                var hours = (long)Math.Floor(deltaMs / (60 * 60 * 1000.0));
                if (hours <= 0) return "just now";
                return $"{hours} hour{(hours == 1 ? "" : "s")} ago";
            }

            if (days < 30) return $"{days} day{(days == 1 ? "" : "s")} ago";
            var months = days / 30;
            if (months < 12) return $"{months} month{(months == 1 ? "" : "s")} ago";
            var years = days / 365;
            return $"{years} year{(years == 1 ? "" : "s")} ago";
        }

        /// <summary>
        /// Pick the version to preselect: the newest one (respecting the prerelease filter) that is at
        /// least <paramref name="minAgeDays"/> old. Falls back to the newest available version when
        /// every candidate is too new or no publish dates are known. <paramref name="versions"/> is
        /// assumed newest-first.
        /// </summary>
        public static string PickDefaultVersion(
            IReadOnlyList<VersionInfo> versions,
            bool includePrerelease,
            int minAgeDays,
            DateTimeOffset? now = null)
        {
            var candidates = versions.Where(v => includePrerelease || !v.IsPrerelease).ToList();
            var pool = candidates.Count > 0 ? candidates : versions.ToList();
            if (pool.Count == 0) return "";

            if (minAgeDays > 0)
            {
                var oldEnough = pool.FirstOrDefault(v => AgeInDays(v.Published, now) >= minAgeDays);
                if (oldEnough != null) return oldEnough.Version;
            }

            return pool[0].Version;
        }
    }
}
