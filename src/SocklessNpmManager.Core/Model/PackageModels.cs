using System.Collections.Generic;

namespace SocklessNpmManager.Core.Model
{
    /// <summary>
    /// One resolved <c>npm audit</c> advisory. <see cref="Range"/> / <see cref="Title"/> come from the
    /// advisory that actually carries the CVE — which, for a package only vulnerable because of a
    /// dependency several hops down its own tree, may not be the package this advisory is attached to.
    /// </summary>
    public sealed class VulnerabilityInfo
    {
        /// <summary>0 Low, 1 Moderate, 2 High, 3 Critical.</summary>
        public int Severity { get; set; }

        public string AdvisoryUrl { get; set; } = "";

        public string? Title { get; set; }

        /// <summary>The vulnerable version range this specific advisory applies to.</summary>
        public string? Range { get; set; }
    }

    public sealed class PackageSummary
    {
        public string Id { get; set; } = "";
        public string Version { get; set; } = "";
        public string Description { get; set; } = "";
        public IReadOnlyList<string> Authors { get; set; } = System.Array.Empty<string>();
        public string? IconUrl { get; set; }
        public long? TotalDownloads { get; set; }
        public bool? Verified { get; set; }
        public string? ProjectUrl { get; set; }
        public string? LicenseUrl { get; set; }
        public string? LicenseExpression { get; set; }
        public IReadOnlyList<string>? Tags { get; set; }

        /// <summary>Source registry name this result came from.</summary>
        public string Source { get; set; } = "";

        /// <summary>Publish date of <see cref="Version"/> (ISO 8601), when known.</summary>
        public string? LatestPublished { get; set; }
    }

    public sealed class PackageDependency
    {
        public string Id { get; set; } = "";
        public string Range { get; set; } = "";
    }

    public sealed class PackageDependencyGroup
    {
        /// <summary><c>dependencies</c> | <c>peerDependencies</c> | <c>optionalDependencies</c>.</summary>
        public string Kind { get; set; } = "";

        public IReadOnlyList<PackageDependency> Dependencies { get; set; } = System.Array.Empty<PackageDependency>();
    }

    public sealed class DeprecationInfo
    {
        public IReadOnlyList<string> Reasons { get; set; } = System.Array.Empty<string>();
        public string? Message { get; set; }
        public string? AlternatePackageId { get; set; }
    }

    public sealed class VersionInfo
    {
        public string Version { get; set; } = "";
        public bool IsPrerelease { get; set; }
        public long? Downloads { get; set; }

        /// <summary>Publish date (ISO 8601), when known.</summary>
        public string? Published { get; set; }
    }

    public sealed class PackageDetail
    {
        public string Id { get; set; } = "";

        /// <summary>All versions, already sorted newest-first by semver rules.</summary>
        public IReadOnlyList<VersionInfo> Versions { get; set; } = System.Array.Empty<VersionInfo>();

        public string SelectedVersion { get; set; } = "";
        public string Description { get; set; } = "";
        public IReadOnlyList<string> Authors { get; set; } = System.Array.Empty<string>();
        public string? IconUrl { get; set; }
        public string? ProjectUrl { get; set; }
        public string? LicenseUrl { get; set; }
        public string? LicenseExpression { get; set; }
        public string? ReadmeMarkdown { get; set; }
        public IReadOnlyList<string> Tags { get; set; } = System.Array.Empty<string>();
        public IReadOnlyList<PackageDependencyGroup> DependencyGroups { get; set; } = System.Array.Empty<PackageDependencyGroup>();
        public DeprecationInfo? Deprecation { get; set; }
        public IReadOnlyList<VulnerabilityInfo>? Vulnerabilities { get; set; }
        public string Source { get; set; } = "";
    }

    public sealed class RegistryInfo
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public bool Enabled { get; set; }
        public bool RequiresAuth { get; set; }
    }

    public sealed class ProjectInfo
    {
        /// <summary>Absolute path to <c>package.json</c>.</summary>
        public string Path { get; set; } = "";

        public string Name { get; set; } = "";

        /// <summary>Path to the root <c>package.json</c> of the workspace that contains this one, if any.</summary>
        public string? WorkspaceRoot { get; set; }

        public PackageManagerName PackageManager { get; set; } = PackageManagerName.Npm;

        public bool IsWorkspaceRoot { get; set; }
    }
}
