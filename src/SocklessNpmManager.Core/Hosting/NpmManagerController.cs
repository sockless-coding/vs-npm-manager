using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SocklessNpmManager.Core.Cli;
using SocklessNpmManager.Core.Model;
using SocklessNpmManager.Core.Npm;
using SocklessNpmManager.Core.Projects;

namespace SocklessNpmManager.Core.Hosting
{
    /// <summary>
    /// Host-agnostic orchestrator. Port of the <c>Controller</c> class in <c>src/extension.ts</c>,
    /// with the webview message protocol collapsed into a normal async API plus .NET events. A host
    /// UI constructs one of these with an <see cref="IHostBridge"/> and calls it directly.
    /// </summary>
    public sealed class NpmManagerController : IDisposable
    {
        private const string AllRegistries = "All registries";

        private readonly IHostBridge _host;
        private readonly NpmHttpClient _http;
        private readonly RegistryService _registries;
        private readonly SearchService _search;
        private readonly MetadataService _metadata;
        private readonly PackageManagerCli _cli;
        private readonly ProjectRegistry _projects;
        private readonly InstalledService _installed;
        private readonly MutationService _mutations;

        private IReadOnlyList<string> _openScope = Array.Empty<string>();

        public NpmManagerController(IHostBridge host)
        {
            _host = host;
            _registries = new RegistryService(host);
            _http = new NpmHttpClient(url => _registries.GetAuthHeaderAsync(url));
            _search = new SearchService(_http);
            _metadata = new MetadataService(_http);
            _cli = new PackageManagerCli(host);
            _projects = new ProjectRegistry(host);
            _installed = new InstalledService(_projects, _cli, _registries, _metadata, host.Config, new NotifierAdapter(this));
            _mutations = new MutationService(_projects, _cli, host);

            _projects.DidChange += (_, _) =>
            {
                _installed.Invalidate();
                ProjectsChanged?.Invoke();
            };

            _host.Config.ConfigChanged += (_, _) =>
            {
                _registries.Refresh();
                _http.ClearCache();
                _cli.InvalidateAvailability();
                _installed.Invalidate();
                SettingsChanged?.Invoke();
            };

            _host.ScopeChanged += async (_, _) =>
            {
                _installed.Invalidate();
                await _projects.RefreshAsync().ConfigureAwait(false);
                RecomputeOpenScope();
                ScopeChanged?.Invoke(_openScope);
            };
        }

        /* ------------------------------- events -------------------------------- */

        public event Action<EnrichPhase, IReadOnlyList<InstalledPackage>>? InstalledEnriched;
        public event Action<string, bool>? Progress;
        public event Action? ProjectsChanged;
        public event Action? InstalledChanged;
        public event Action? SettingsChanged;
        public event Action<IReadOnlyList<string>>? ScopeChanged;

        /* ----------------------------- lifecycle ------------------------------- */

        public async Task InitializeAsync()
        {
            _registries.Refresh();
            _projects.Start();
            await _projects.RefreshAsync().ConfigureAwait(false);
            RecomputeOpenScope();
        }

        public async Task RefreshAsync()
        {
            _registries.Refresh();
            _http.ClearCache();
            _installed.Invalidate();
            await _projects.RefreshAsync().ConfigureAwait(false);
            InstalledChanged?.Invoke();
        }

        /// <summary>package.json paths to preselect, derived from the node the manager was opened from.</summary>
        public IReadOnlyList<string> OpenScope => _openScope;

        private void RecomputeOpenScope() => _openScope = _projects.ResolveSelectionScope(_host.GetScope());

        /* ------------------------------ requests ------------------------------- */

        public Task<InitialState> GetInitialStateAsync() => Task.FromResult(BuildInitialState());

        public IReadOnlyList<RegistryInfo> ListRegistries() => RegistryInfos();

        public IReadOnlyList<ProjectInfo> ListProjects() => _projects.GetProjects().Select(p => p.Info).ToList();

        public async Task<SearchPage> SearchAsync(string query, int skip, int take, bool includePrerelease, string source, CancellationToken cancellationToken = default)
        {
            var targets = TargetRegistries(source);
            var lists = await Task.WhenAll(targets.Select(async registry =>
            {
                try
                {
                    return await WithAuthRetryAsync(registry.Name, () => _search.SearchAsync(registry.Url, registry.Name, new SearchOptions
                    {
                        Query = query,
                        Skip = skip,
                        Take = take,
                        IncludePrerelease = includePrerelease,
                    }, cancellationToken)).ConfigureAwait(false);
                }
                catch
                {
                    return new SearchPage();
                }
            })).ConfigureAwait(false);

            return new SearchPage
            {
                Results = SearchService.MergeSearchResults(lists.Select(l => l.Results)),
                HasMore = lists.Any(l => l.HasMore),
            };
        }

        public async Task<PackageDetail> GetPackageDetailAsync(string packageId, string source, bool includePrerelease, CancellationToken cancellationToken = default)
        {
            var targets = TargetRegistries(source);
            var registry = targets.FirstOrDefault()
                           ?? _registries.RegistryForPackage(packageId)
                           ?? _registries.GetEnabledRegistries().FirstOrDefault();
            if (registry == null) throw new InvalidOperationException("No npm registry is configured.");

            return await WithAuthRetryAsync(registry.Name, () =>
                _metadata.GetPackageDetailAsync(registry.Url, registry.Name, packageId, includePrerelease, cancellationToken)).ConfigureAwait(false);
        }

        public Task<(IReadOnlyList<InstalledPackage> Packages, bool PackageManagerAvailable)> ListInstalledAsync(bool includeTransitive) =>
            _installed.ListAsync(includeTransitive);

        public async Task<MutationResult> MutateAsync(MutationRequest request)
        {
            var registry = string.IsNullOrEmpty(request.Source) ? null : _registries.FindByName(request.Source!);
            var result = await _mutations.ApplyAsync(request, registry?.Url).ConfigureAwait(false);
            InstalledChanged?.Invoke();
            return result;
        }

        public Task OpenExternalAsync(string url) => _host.OpenExternalAsync(url);

        /* ------------------------------ helpers -------------------------------- */

        private InitialState BuildInitialState() => new InitialState
        {
            DefaultIncludePrerelease = _host.Config.GetBool(SettingKeys.DefaultIncludePrerelease, false),
            Registries = RegistryInfos(),
            Projects = _projects.GetProjects().Select(p => p.Info).ToList(),
            MinimumPackageAgeDays = MinimumPackageAgeDays(),
            PreselectProjectPaths = _openScope,
        };

        private int MinimumPackageAgeDays()
        {
            var raw = _host.Config.GetInt(SettingKeys.MinimumPackageAgeDays, 7);
            return raw > 0 ? raw : 0;
        }

        private IReadOnlyList<RegistryInfo> RegistryInfos() => _registries.GetRegistries().Select(r => new RegistryInfo
        {
            Name = r.Name,
            Url = r.Url,
            Enabled = r.Enabled,
            RequiresAuth = r.HasAuth,
        }).ToList();

        private List<Registry> TargetRegistries(string source)
        {
            var enabled = _registries.GetEnabledRegistries().ToList();
            if (!string.IsNullOrEmpty(source) && source != AllRegistries)
            {
                return enabled.Where(r => r.Name == source).ToList();
            }

            return enabled;
        }

        /// <summary>Run <paramref name="fn"/>; on a 401/403 prompt for credentials once and retry.</summary>
        private async Task<T> WithAuthRetryAsync<T>(string registryName, Func<Task<T>> fn)
        {
            try
            {
                return await fn().ConfigureAwait(false);
            }
            catch (HttpError err) when (err.Status == 401 || err.Status == 403)
            {
                if (await _registries.PromptForCredentialsAsync(registryName).ConfigureAwait(false))
                {
                    _http.ClearCache();
                    return await fn().ConfigureAwait(false);
                }

                throw;
            }
        }

        public void Dispose()
        {
            _projects.Dispose();
            _http.Dispose();
        }

        private sealed class NotifierAdapter : IInstalledNotifier
        {
            private readonly NpmManagerController _owner;

            public NotifierAdapter(NpmManagerController owner) => _owner = owner;

            public void Progress(string message, bool done) => _owner.Progress?.Invoke(message, done);

            public void Enriched(EnrichPhase phase, IReadOnlyList<InstalledPackage> packages) =>
                _owner.InstalledEnriched?.Invoke(phase, packages);
        }
    }
}
