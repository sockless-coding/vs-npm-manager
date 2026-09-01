using System;
using System.Collections.Generic;

namespace SocklessNpmManager.Core.Model
{
    public sealed class MutationRequest
    {
        public InstallAction Action { get; set; }
        public string PackageId { get; set; } = "";
        public string? Version { get; set; }
        public IReadOnlyList<string> ProjectPaths { get; set; } = Array.Empty<string>();
        public string? Source { get; set; }

        /// <summary>Where a fresh install should be written; ignored for update/uninstall/pin/unpin.</summary>
        public DependencyType? DependencyType { get; set; }

        /// <summary>
        /// How to write <see cref="Version"/>; only used for install/update — pin/unpin always use
        /// exact/caret. Defaults to <see cref="VersionPrefix.Caret"/>.
        /// </summary>
        public VersionPrefix? VersionPrefix { get; set; }
    }

    public sealed class ProjectMutationResult
    {
        public string Project { get; set; } = "";
        public bool Ok { get; set; }
        public string? Message { get; set; }
    }

    public sealed class MutationResult
    {
        public bool Ok { get; set; } = true;
        public InstallAction Action { get; set; }
        public string PackageId { get; set; } = "";
        public List<ProjectMutationResult> PerProject { get; set; } = new List<ProjectMutationResult>();
        public bool UsedFallback { get; set; }
        public bool InstallNeeded { get; set; }
    }

    public sealed class InitialState
    {
        public bool DefaultIncludePrerelease { get; set; }
        public IReadOnlyList<RegistryInfo> Registries { get; set; } = Array.Empty<RegistryInfo>();
        public IReadOnlyList<ProjectInfo> Projects { get; set; } = Array.Empty<ProjectInfo>();

        /// <summary>Minimum age in days before a package version is trusted; 0 disables the check.</summary>
        public int MinimumPackageAgeDays { get; set; }

        /// <summary>
        /// <c>package.json</c> paths to preselect for install/update, based on the node the manager
        /// was opened from. Empty when opened without a specific scope.
        /// </summary>
        public IReadOnlyList<string> PreselectProjectPaths { get; set; } = Array.Empty<string>();
    }
}
