using System.Collections.Generic;
using System.Text.RegularExpressions;
using SocklessNpmManager.Core.Model;
using SemRange = SemanticVersioning.Range;
using SemVersion = SemanticVersioning.Version;

namespace SocklessNpmManager.Core.Npm
{
    /// <summary>
    /// Helpers for npm exact-version "pins" and the small set of version selectors the manager
    /// exposes. Port of <c>src/npm/versionRange.ts</c>.
    ///
    /// A dependency written as a bare version (<c>"1.2.3"</c>, no <c>^</c>/<c>~</c>/comparator) is
    /// locked to precisely that version. The manager treats such a reference as "pinned": held back
    /// from "Update All" and not offered as the default upgrade target. Pinning never suppresses
    /// vulnerability checks.
    /// </summary>
    public static class VersionRange
    {
        private static readonly Regex RangeOperators = new Regex(@"^[\^~]|[<>=* ]|\|\|", RegexOptions.Compiled);
        private static readonly Regex LeadingVersion = new Regex(@"^[\^~]?(\d[\w.\-+]*)", RegexOptions.Compiled);

        /// <summary>True when <paramref name="raw"/> is a bare exact version such as <c>1.2.3</c>, with no range operator.</summary>
        public static bool IsExactVersionPin(string? raw)
        {
            if (raw == null) return false;
            var v = raw.Trim();
            if (v.Length == 0) return false;
            if (!SemVersion.TryParse(v, loose: false, out _)) return false;
            return !RangeOperators.IsMatch(v);
        }

        /// <summary>
        /// The base version out of a range/pin (<c>^1.2.3</c> → <c>1.2.3</c>, <c>1.2.3</c> → <c>1.2.3</c>);
        /// best-effort for anything else.
        /// </summary>
        public static string StripVersionPin(string? raw)
        {
            if (raw == null) return "";
            var v = raw.Trim();
            if (v.Length == 0) return "";
            if (SemVersion.TryParse(v, loose: false, out _)) return v;

            var m = LeadingVersion.Match(v);
            if (m.Success && SemVersion.TryParse(m.Groups[1].Value, loose: false, out _)) return m.Groups[1].Value;

            return MinVersion(v) ?? v;
        }

        /// <summary>Wrap a plain version as an exact pin: <c>^1.2.3</c> → <c>1.2.3</c>. Idempotent.</summary>
        public static string ToExactVersionPin(string version)
        {
            var stripped = StripVersionPin(version);
            return stripped.Length > 0 ? stripped : version.Trim();
        }

        /// <summary>Wrap a plain version as a caret range for "unpin": <c>1.2.3</c> → <c>^1.2.3</c>.</summary>
        public static string ToCaretRange(string version)
        {
            var stripped = StripVersionPin(version);
            var b = stripped.Length > 0 ? stripped : version.Trim();
            return b.Length > 0 ? "^" + b : b;
        }

        /// <summary>Write <paramref name="version"/> using the given selector.</summary>
        public static string ApplyVersionPrefix(string version, VersionPrefix prefix)
        {
            var stripped = StripVersionPin(version);
            var b = stripped.Length > 0 ? stripped : version.Trim();
            if (b.Length == 0) return b;
            switch (prefix)
            {
                case VersionPrefix.Exact: return b;
                case VersionPrefix.Caret: return "^" + b;
                case VersionPrefix.Tilde: return "~" + b;
                case VersionPrefix.Gte: return ">=" + b;
                default: return "^" + b;
            }
        }

        /// <summary>Best-effort selector a currently-written range was created with; defaults to caret.</summary>
        public static VersionPrefix DetectVersionPrefix(string? raw)
        {
            if (raw == null) return VersionPrefix.Caret;
            var v = raw.Trim();
            if (v.StartsWith("^")) return VersionPrefix.Caret;
            if (v.StartsWith("~")) return VersionPrefix.Tilde;
            if (v.StartsWith(">=")) return VersionPrefix.Gte;
            if (IsExactVersionPin(v)) return VersionPrefix.Exact;
            return VersionPrefix.Caret;
        }

        /// <summary>Not a resolvable semver range at all — <c>workspace:*</c>, <c>file:../x</c>, <c>git+https://…</c>, etc.</summary>
        public static bool IsUnresolvableRange(string? raw)
        {
            if (raw == null) return true;
            var v = raw.Trim();
            if (v.Length == 0) return true;
            return !SemRange.TryParse(v, out _);
        }

        private static readonly Regex NumericStart = new Regex(@"(\d[\w.\-+]*)", RegexOptions.Compiled);

        /// <summary>Approximation of node-semver's <c>minVersion</c> for the rare fallback path.</summary>
        private static string? MinVersion(string range)
        {
            var m = NumericStart.Match(range);
            if (m.Success && SemVersion.TryParse(m.Groups[1].Value, loose: true, out var parsed))
            {
                return parsed.ToString();
            }

            return null;
        }
    }
}
