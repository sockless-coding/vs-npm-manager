namespace SocklessNpmManager.Core.Model
{
    /// <summary>A <c>package.json</c> dependency section. String forms match the JSON keys.</summary>
    public enum DependencyType
    {
        Dependencies,
        DevDependencies,
        PeerDependencies,
        OptionalDependencies,
    }

    /// <summary>
    /// How a chosen version is written to <c>package.json</c>: <c>^1.2.3</c>, <c>~1.2.3</c>,
    /// <c>1.2.3</c>, or <c>&gt;=1.2.3</c>.
    /// </summary>
    public enum VersionPrefix
    {
        Exact,
        Caret,
        Tilde,
        Gte,
    }

    public enum InstallAction
    {
        Install,
        Update,
        Uninstall,
        Pin,
        Unpin,
    }

    public enum PackageManagerName
    {
        Npm,
        Yarn,
        Pnpm,
    }

    /// <summary>How the manager was opened, mirroring the VS Code multi-root / workspace-root behaviour.</summary>
    public enum ScopeMode
    {
        /// <summary>No specific scope — every discovered project (command palette / generic open).</summary>
        Workspace,

        /// <summary>Opened from the solution node — all projects in the solution.</summary>
        Solution,

        /// <summary>Opened from a single project / <c>package.json</c> — limited to that project (+ its workspace members).</summary>
        Project,
    }

    /// <summary>Phase of the streamed <see cref="InstalledService"/> enrichment.</summary>
    public enum EnrichPhase
    {
        Updates,
        Vulnerabilities,
        Done,
    }

    public static class DependencyTypeExtensions
    {
        /// <summary>The <c>package.json</c> key: <c>dependencies</c>, <c>devDependencies</c>, …</summary>
        public static string ToJsonKey(this DependencyType type)
        {
            switch (type)
            {
                case DependencyType.Dependencies: return "dependencies";
                case DependencyType.DevDependencies: return "devDependencies";
                case DependencyType.PeerDependencies: return "peerDependencies";
                case DependencyType.OptionalDependencies: return "optionalDependencies";
                default: return "dependencies";
            }
        }

        public static bool TryFromJsonKey(string? key, out DependencyType type)
        {
            switch (key)
            {
                case "dependencies": type = DependencyType.Dependencies; return true;
                case "devDependencies": type = DependencyType.DevDependencies; return true;
                case "peerDependencies": type = DependencyType.PeerDependencies; return true;
                case "optionalDependencies": type = DependencyType.OptionalDependencies; return true;
                default: type = DependencyType.Dependencies; return false;
            }
        }
    }

    public static class PackageManagerNameExtensions
    {
        public static string ToCliName(this PackageManagerName pm)
        {
            switch (pm)
            {
                case PackageManagerName.Yarn: return "yarn";
                case PackageManagerName.Pnpm: return "pnpm";
                default: return "npm";
            }
        }
    }
}
