using System;
using System.Collections.Generic;
using System.Linq;
using SemVersion = SemanticVersioning.Version;

namespace SocklessNpmManager.Core.Npm
{
    /// <summary>
    /// The handful of semver operations the manager needs: descending sort (unparseable entries
    /// last), prerelease detection, and picking the highest version subject to a prerelease filter.
    /// Port of <c>src/npm/semverUtil.ts</c>.
    /// </summary>
    public static class SemverUtil
    {
        public static bool IsValid(string? version)
        {
            return version != null && SemVersion.TryParse(version.Trim(), loose: true, out _);
        }

        public static bool IsPrerelease(string? version)
        {
            if (version == null) return false;
            return SemVersion.TryParse(version.Trim(), loose: true, out var parsed) && parsed.IsPreRelease;
        }

        /// <summary>
        /// Newest-first. Versions that cannot be parsed sort after every valid one, keeping their
        /// original relative order.
        /// </summary>
        public static List<string> SortVersionsDescending(IEnumerable<string> versions)
        {
            var valid = new List<(string raw, SemVersion parsed)>();
            var invalid = new List<string>();
            foreach (var v in versions)
            {
                if (SemVersion.TryParse(v, loose: true, out var parsed))
                {
                    valid.Add((v, parsed));
                }
                else
                {
                    invalid.Add(v);
                }
            }

            // Stable descending sort by semver precedence.
            var sorted = valid
                .Select((entry, index) => (entry.raw, entry.parsed, index))
                .OrderByDescending(x => x.parsed)
                .ThenBy(x => x.index)
                .Select(x => x.raw);

            var result = new List<string>();
            result.AddRange(sorted);
            result.AddRange(invalid);
            return result;
        }

        /// <summary>Highest version, optionally excluding prereleases; <c>null</c> when nothing qualifies.</summary>
        public static string? MaxVersion(IEnumerable<string> versions, bool includePrerelease)
        {
            SemVersion? best = null;
            string? bestRaw = null;
            foreach (var v in versions)
            {
                if (!SemVersion.TryParse(v, loose: true, out var parsed)) continue;
                if (!includePrerelease && parsed.IsPreRelease) continue;
                if (best == null || parsed > best)
                {
                    best = parsed;
                    bestRaw = v;
                }
            }

            return bestRaw;
        }

        /// <summary>Compare two version strings by semver precedence (loose). Unparseable sorts lowest.</summary>
        public static int Compare(string a, string b)
        {
            var aOk = SemVersion.TryParse(a, loose: true, out var av);
            var bOk = SemVersion.TryParse(b, loose: true, out var bv);
            if (aOk && bOk) return av.CompareTo(bv);
            if (aOk) return 1;
            if (bOk) return -1;
            return string.CompareOrdinal(a, b);
        }
    }
}
