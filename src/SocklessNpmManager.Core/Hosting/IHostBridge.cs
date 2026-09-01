using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SocklessNpmManager.Core.Model;

namespace SocklessNpmManager.Core.Hosting
{
    /// <summary>
    /// Everything the Core needs from a host IDE. A new IDE target implements this interface and
    /// nothing in Core changes. Port of the surface the VS Code <c>extension.ts</c> reached for
    /// (<c>workspace.getConfiguration</c>, <c>context.secrets</c>, <c>OutputChannel</c>,
    /// <c>window.showInputBox</c>, <c>env.openExternal</c>, workspace folders + file watchers).
    /// </summary>
    public interface IHostBridge
    {
        IHostConfig Config { get; }
        IHostSecrets Secrets { get; }
        IHostLogger Logger { get; }

        /// <summary>The scope the manager was opened with (project / solution / generic).</summary>
        HostScope GetScope();

        /// <summary>Raised when a command re-opens the manager from a different node.</summary>
        event EventHandler? ScopeChanged;

        /// <summary>Watch <paramref name="globs"/> (package.json / lockfiles) under the scope roots. Debounced by the host.</summary>
        IDisposable WatchFiles(IEnumerable<string> globs, Action onChanged);

        /// <summary>Ask the user for a value (used for registry credentials). <c>null</c> when cancelled.</summary>
        Task<string?> PromptAsync(string title, string prompt, bool password, CancellationToken cancellationToken = default);

        /// <summary>Open a URL in the system browser.</summary>
        Task OpenExternalAsync(string url);

        /// <summary>A sensible working directory when none is implied (first scope root, else the process cwd).</summary>
        string Cwd();
    }

    public interface IHostConfig
    {
        bool GetBool(string key, bool fallback);
        int GetInt(string key, int fallback);
        string GetString(string key, string fallback);

        /// <summary>Additional registries configured in host settings (<c>npmManager.additionalRegistries</c>).</summary>
        IReadOnlyList<AdditionalRegistry> GetAdditionalRegistries();

        event EventHandler? ConfigChanged;
    }

    public interface IHostSecrets
    {
        Task<string?> GetAsync(string key);
        Task StoreAsync(string key, string value);
        Task DeleteAsync(string key);
    }

    public interface IHostLogger
    {
        void Line(string message);
        void Append(string message);
    }

    public sealed class AdditionalRegistry
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
    }

    public sealed class HostScope
    {
        public ScopeMode Mode { get; set; } = ScopeMode.Workspace;

        /// <summary>Directories to scan for <c>package.json</c>. Empty means "the host cwd".</summary>
        public IReadOnlyList<string> Roots { get; set; } = Array.Empty<string>();

        public static readonly HostScope Empty = new HostScope();
    }

    /// <summary>Well-known <c>npmManager.*</c> setting keys, matching the VS Code extension.</summary>
    public static class SettingKeys
    {
        public const string DefaultIncludePrerelease = "npmManager.defaultIncludePrerelease";
        public const string PackageManagerPath = "npmManager.packageManagerPath";
        public const string NodePath = "npmManager.nodePath";
        public const string AutoInstall = "npmManager.autoInstall";
        public const string MinimumPackageAgeDays = "npmManager.minimumPackageAgeDays";
        public const string UsePackageManagerForEnumeration = "npmManager.usePackageManagerForEnumeration";
    }
}
