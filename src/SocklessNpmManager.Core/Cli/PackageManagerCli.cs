using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SocklessNpmManager.Core.Hosting;
using SocklessNpmManager.Core.Model;
using SocklessNpmManager.Core.Npm;
using SocklessNpmManager.Core.Projects;

namespace SocklessNpmManager.Core.Cli
{
    /// <summary>
    /// Wrapper around the npm / yarn (classic) / pnpm CLIs. Port of <c>src/node/cli.ts</c>.
    /// </summary>
    public sealed class PackageManagerCli
    {
        private static readonly Dictionary<DependencyType, string> SaveFlag = new Dictionary<DependencyType, string>
        {
            [DependencyType.Dependencies] = "--save-prod",
            [DependencyType.DevDependencies] = "--save-dev",
            [DependencyType.PeerDependencies] = "--save-peer",
            [DependencyType.OptionalDependencies] = "--save-optional",
        };

        private static readonly Dictionary<PackageManagerName, HashSet<DependencyType>> SupportedAddTypes =
            new Dictionary<PackageManagerName, HashSet<DependencyType>>
            {
                [PackageManagerName.Npm] = new HashSet<DependencyType> { DependencyType.Dependencies, DependencyType.DevDependencies, DependencyType.PeerDependencies, DependencyType.OptionalDependencies },
                [PackageManagerName.Pnpm] = new HashSet<DependencyType> { DependencyType.Dependencies, DependencyType.DevDependencies, DependencyType.PeerDependencies, DependencyType.OptionalDependencies },
                [PackageManagerName.Yarn] = new HashSet<DependencyType> { DependencyType.Dependencies, DependencyType.DevDependencies },
            };

        private readonly IHostBridge _host;
        private readonly ConcurrentDictionary<PackageManagerName, bool> _availability = new ConcurrentDictionary<PackageManagerName, bool>();

        public PackageManagerCli(IHostBridge host)
        {
            _host = host;
        }

        public bool SupportsAddType(PackageManagerName pm, DependencyType type) => SupportedAddTypes[pm].Contains(type);

        /// <summary>Detect the package manager for a project directory from its lockfile, walking up to the workspace root.</summary>
        public static PackageManagerName DetectPackageManager(string projectDir, string workspaceRoot)
        {
            var dir = projectDir;
            while (true)
            {
                if (File.Exists(Path.Combine(dir, "pnpm-lock.yaml"))) return PackageManagerName.Pnpm;
                if (File.Exists(Path.Combine(dir, "yarn.lock"))) return PackageManagerName.Yarn;
                if (File.Exists(Path.Combine(dir, "package-lock.json"))) return PackageManagerName.Npm;
                var parent = Path.GetDirectoryName(dir);
                if (dir == workspaceRoot || string.IsNullOrEmpty(parent) || parent == dir) break;
                dir = parent;
            }

            return PackageManagerName.Npm;
        }

        private string ExeFor(PackageManagerName pm)
        {
            var overridePath = _host.Config.GetString(SettingKeys.PackageManagerPath, "").Trim();
            return overridePath.Length > 0 ? overridePath : pm.ToCliName();
        }

        public async Task<bool> IsAvailableAsync(PackageManagerName pm)
        {
            if (_availability.TryGetValue(pm, out var cached)) return cached;
            bool ok;
            try
            {
                var r = await RunAsync(pm, new[] { "--version" }, null, quiet: true).ConfigureAwait(false);
                ok = r.Code == 0;
            }
            catch
            {
                ok = false;
            }

            _availability[pm] = ok;
            return ok;
        }

        public void InvalidateAvailability() => _availability.Clear();

        public async Task<RunResult> RunAsync(PackageManagerName pm, IReadOnlyList<string> args, string? cwd = null, bool quiet = false, CancellationToken cancellationToken = default)
        {
            var scope = _host.GetScope();
            var workingDir = cwd ?? (scope.Roots.Count > 0 ? scope.Roots[0] : _host.Cwd());
            var exe = ExeFor(pm);

            if (!quiet) _host.Logger.Line($"> {pm.ToCliName()} {string.Join(" ", args)}  ({workingDir})");

            RunResult result;
            try
            {
                result = await ProcessRunner.RunAsync(exe, args, workingDir, cancellationToken).ConfigureAwait(false);
            }
            catch (ExecutableNotFoundException)
            {
                throw new ExecutableNotFoundException(exe,
                    $"'{exe}' was not found. Install {pm.ToCliName()} or set '{SettingKeys.PackageManagerPath}'.");
            }

            if (!quiet)
            {
                if (result.Stdout.Length > 0) _host.Logger.Append(result.Stdout);
                if (result.Stderr.Length > 0) _host.Logger.Append(result.Stderr);
            }

            return result;
        }

        public Task<RunResult> AddPackageAsync(
            PackageManagerName pm,
            string projectDir,
            string id,
            string? version,
            DependencyType dependencyType,
            bool exact,
            string? registryUrl = null,
            CancellationToken cancellationToken = default)
        {
            var spec = string.IsNullOrEmpty(version) ? id : $"{id}@{version}";
            var args = new List<string>();

            if (pm == PackageManagerName.Npm)
            {
                args.Add("install");
                args.Add(spec);
                args.Add(SaveFlag[dependencyType]);
                if (exact) args.Add("--save-exact");
                if (!string.IsNullOrEmpty(registryUrl)) { args.Add("--registry"); args.Add(registryUrl!); }
                return RunAsync(pm, args, projectDir, cancellationToken: cancellationToken);
            }

            if (pm == PackageManagerName.Pnpm)
            {
                args.Add("add");
                args.Add(spec);
                if (dependencyType == DependencyType.DevDependencies) args.Add("--save-dev");
                else if (dependencyType == DependencyType.PeerDependencies) args.Add("--save-peer");
                else if (dependencyType == DependencyType.OptionalDependencies) args.Add("--save-optional");
                if (exact) args.Add("--save-exact");
                if (!string.IsNullOrEmpty(registryUrl)) { args.Add("--registry"); args.Add(registryUrl!); }
                return RunAsync(pm, args, projectDir, cancellationToken: cancellationToken);
            }

            // yarn classic
            args.Add("add");
            args.Add(spec);
            if (dependencyType == DependencyType.DevDependencies) args.Add("--dev");
            if (exact) args.Add("--exact");
            if (!string.IsNullOrEmpty(registryUrl)) { args.Add("--registry"); args.Add(registryUrl!); }
            return RunAsync(pm, args, projectDir, cancellationToken: cancellationToken);
        }

        public Task<RunResult> RemovePackageAsync(PackageManagerName pm, string projectDir, string id, CancellationToken cancellationToken = default)
        {
            var verb = pm == PackageManagerName.Npm ? "uninstall" : "remove";
            return RunAsync(pm, new[] { verb, id }, projectDir, cancellationToken: cancellationToken);
        }

        public Task<RunResult> InstallAsync(PackageManagerName pm, string projectDir, CancellationToken cancellationToken = default) =>
            RunAsync(pm, new[] { "install" }, projectDir, cancellationToken: cancellationToken);

        /// <summary><c>npm ls --all --json</c>; <c>null</c> on any non-JSON failure. Only meaningful for npm.</summary>
        public async Task<JsonElement?> ListPackagesAsync(string projectDir, CancellationToken cancellationToken = default)
        {
            var r = await RunAsync(PackageManagerName.Npm, new[] { "ls", "--all", "--json" }, projectDir, quiet: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ParseJsonLenient(r.Stdout);
        }

        /// <summary><c>npm outdated --json</c>.</summary>
        public async Task<Dictionary<string, NpmOutdatedEntry>?> OutdatedAsync(string projectDir, CancellationToken cancellationToken = default)
        {
            var r = await RunAsync(PackageManagerName.Npm, new[] { "outdated", "--json" }, projectDir, quiet: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(r.Stdout)) return new Dictionary<string, NpmOutdatedEntry>();
            var element = ParseJsonLenient(r.Stdout);
            if (element == null) return null;
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, NpmOutdatedEntry>>(element.Value.GetRawText(), NpmHttpClient.JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        /// <summary><c>npm audit --json</c>.</summary>
        public async Task<NpmAuditOutput?> AuditAsync(string projectDir, CancellationToken cancellationToken = default)
        {
            var r = await RunAsync(PackageManagerName.Npm, new[] { "audit", "--json" }, projectDir, quiet: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            var element = ParseJsonLenient(r.Stdout);
            if (element == null) return null;
            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<NpmAuditOutput>(element.Value.GetRawText());
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// <c>npm --json</c> output should be pure JSON but some npm versions/proxies prepend or trail
        /// non-JSON text. Parse from the first <c>{</c> to the last <c>}</c>.
        /// </summary>
        private static JsonElement? ParseJsonLenient(string stdout)
        {
            var start = stdout.IndexOf('{');
            var end = stdout.LastIndexOf('}');
            if (start < 0 || end < start) return null;
            try
            {
                using var doc = JsonDocument.Parse(stdout.Substring(start, end - start + 1));
                return doc.RootElement.Clone();
            }
            catch
            {
                return null;
            }
        }
    }

    public sealed class NpmOutdatedEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("current")] public string? Current { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("wanted")] public string? Wanted { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("latest")] public string? Latest { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("dependent")] public string? Dependent { get; set; }
    }
}
