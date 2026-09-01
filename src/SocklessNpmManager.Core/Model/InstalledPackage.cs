using System.Collections.Generic;

namespace SocklessNpmManager.Core.Model
{
    /// <summary>A per-project direct reference to a package — the basis for the Consolidate view.</summary>
    public sealed class ProjectVersionRef
    {
        public string Project { get; set; } = "";
        public string Version { get; set; } = "";
        public bool? Pinned { get; set; }
        public DependencyType? DependencyType { get; set; }
    }

    /// <summary>
    /// One row of the Installed view. Mutated in place during background enrichment, mirroring
    /// <c>projects/installed.ts</c>.
    /// </summary>
    public sealed class InstalledPackage
    {
        public string Id { get; set; } = "";

        /// <summary>Requested version / range as written in <c>package.json</c> (e.g. <c>^1.2.3</c>).</summary>
        public string RequestedVersion { get; set; } = "";

        /// <summary>Resolved version from the lockfile / <c>node_modules</c>, when known.</summary>
        public string? ResolvedVersion { get; set; }

        /// <summary><c>package.json</c> paths that reference this package directly.</summary>
        public List<string> Projects { get; set; } = new List<string>();

        /// <summary>Direct reference version per project.</summary>
        public List<ProjectVersionRef> ProjectVersions { get; set; } = new List<ProjectVersionRef>();

        /// <summary>True when only present transitively (not a direct dependency anywhere).</summary>
        public bool Transitive { get; set; }

        public string? LatestVersion { get; set; }
        public string? LatestStableVersion { get; set; }
        public bool? Deprecated { get; set; }
        public bool? HasVulnerability { get; set; }
        public string? IconUrl { get; set; }

        /// <summary>
        /// Known advisories affecting the installed version — including ones that only apply because
        /// of a package deeper in this one's own dependency tree, resolved recursively. Sorted worst-first.
        /// </summary>
        public List<VulnerabilityInfo>? Vulnerabilities { get; set; }

        /// <summary>Highest advisory severity (0..3), or -1 when there are none.</summary>
        public int? MaxVulnerabilitySeverity { get; set; }

        /// <summary>Project paths where the resolved version is flagged vulnerable.</summary>
        public List<string>? VulnerableProjects { get; set; }

        /// <summary>Package ids in the resolved graph that depend directly on this package.</summary>
        public IReadOnlyList<string>? RequiredBy { get; set; }

        /// <summary>Package ids this package depends on directly (resolved graph).</summary>
        public IReadOnlyList<string>? DependsOn { get; set; }

        /// <summary>Publish date of <see cref="LatestVersion"/> (ISO 8601), when known.</summary>
        public string? LatestPublished { get; set; }

        /// <summary>True when <see cref="LatestVersion"/> is newer than the configured minimum package age.</summary>
        public bool? LatestBelowMinAge { get; set; }

        /// <summary>
        /// True when every direct reference is an exact-version pin. Pinned packages are held back
        /// from "Update All"; vulnerability checks still apply.
        /// </summary>
        public bool? Pinned { get; set; }

        /// <summary>The pinned version when all direct references pin the same exact version.</summary>
        public string? PinnedVersion { get; set; }
    }
}
