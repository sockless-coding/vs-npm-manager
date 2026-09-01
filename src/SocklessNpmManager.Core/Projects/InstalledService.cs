using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SocklessNpmManager.Core.Cli;
using SocklessNpmManager.Core.Hosting;
using SocklessNpmManager.Core.Model;
using SocklessNpmManager.Core.Npm;
using SocklessNpmManager.Core.Util;

namespace SocklessNpmManager.Core.Projects
{
    /// <summary>How the host is told about streamed enrichment progress and results.</summary>
    public interface IInstalledNotifier
    {
        void Progress(string message, bool done);
        void Enriched(EnrichPhase phase, IReadOnlyList<InstalledPackage> packages);
    }

    /// <summary>
    /// Builds the "Installed" view and its update / vulnerability enrichment. Port of
    /// <c>src/projects/installed.ts</c>.
    ///
    /// Phase 1 is a local snapshot read entirely from disk. Phase 2 enriches it in the background:
    /// latest version / publish date / deprecation per direct package, then <c>npm audit --json</c>
    /// advisories resolved through the full <c>via</c> chain, then an optional <c>npm outdated</c>
    /// reconcile. Progress and partial results stream through the <see cref="IInstalledNotifier"/>.
    /// </summary>
    public sealed class InstalledService
    {
        private const double DayMs = 24 * 60 * 60 * 1000;

        private readonly ProjectRegistry _projects;
        private readonly PackageManagerCli _cli;
        private readonly RegistryService _registries;
        private readonly MetadataService _metadata;
        private readonly IHostConfig _config;
        private readonly IInstalledNotifier _notify;

        private List<InstalledPackage>? _snapshot;
        private Task<List<InstalledPackage>>? _snapshotTask;
        private int _runToken;
        private Task? _enrichTask;
        private int _enrichToken = -1;
        private bool _lastIncludeTransitive;

        public InstalledService(
            ProjectRegistry projects,
            PackageManagerCli cli,
            RegistryService registries,
            MetadataService metadata,
            IHostConfig config,
            IInstalledNotifier notify)
        {
            _projects = projects;
            _cli = cli;
            _registries = registries;
            _metadata = metadata;
            _config = config;
            _notify = notify;
        }

        /// <summary>Drop cached results; the next <see cref="ListAsync"/> rebuilds from disk.</summary>
        public void Invalidate()
        {
            Interlocked.Increment(ref _runToken);
            _snapshot = null;
            _snapshotTask = null;
            _enrichTask = null;
        }

        public async Task<(IReadOnlyList<InstalledPackage> Packages, bool PackageManagerAvailable)> ListAsync(bool includeTransitive)
        {
            _lastIncludeTransitive = includeTransitive;
            var snap = await EnsureSnapshotAsync().ConfigureAwait(false);
            _ = EnsureEnrichmentAsync();
            return (FilterForView(snap, includeTransitive), await _cli.IsAvailableAsync(PackageManagerName.Npm).ConfigureAwait(false));
        }

        /* --------------------------- phase 1: snapshot --------------------------- */

        private Task<List<InstalledPackage>> EnsureSnapshotAsync()
        {
            if (_snapshot != null) return Task.FromResult(_snapshot);
            if (_snapshotTask == null)
            {
                var token = _runToken;
                _snapshotTask = BuildLocalSnapshotAsync().ContinueWith(t =>
                {
                    if (token == _runToken) _snapshot = t.Result;
                    _snapshotTask = null;
                    return _snapshot ?? t.Result;
                }, TaskScheduler.Default);
            }

            return _snapshotTask;
        }

        private Task<List<InstalledPackage>> BuildLocalSnapshotAsync()
        {
            var projects = _projects.GetProjects();
            var merged = new Dictionary<string, InstalledPackage>(StringComparer.OrdinalIgnoreCase);

            foreach (var project in projects) FoldProjectModel(project, merged);

            var graphByRoot = new Dictionary<string, DependencyGraph>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in projects)
            {
                var rootKey = (LockGraph.FindLockfileRoot(project.Dir, project.WorkspaceRootDir)?.Dir ?? project.WorkspaceRootDir).ToLowerInvariant();
                if (!graphByRoot.ContainsKey(rootKey))
                {
                    graphByRoot[rootKey] = LockGraph.ReadDependencyGraph(project.Dir, project.WorkspaceRootDir);
                }
            }

            var graph = LockGraph.MergeGraphs(graphByRoot.Values);

            foreach (var kv in graph.DisplayName)
            {
                if (merged.ContainsKey(kv.Key)) continue;
                if (!graph.Resolved.ContainsKey(kv.Key)) continue;
                merged[kv.Key] = new InstalledPackage { Id = kv.Value, RequestedVersion = "", Transitive = true };
            }

            foreach (var kv in merged)
            {
                var entry = kv.Value;
                entry.Transitive = entry.ProjectVersions.Count == 0;

                if (graph.Resolved.TryGetValue(kv.Key, out var resolved)) entry.ResolvedVersion = resolved;

                ApplyGraphEdges(entry, kv.Key, graph);
            }

            MarkPinned(merged);

            var list = merged.Values.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList();
            return Task.FromResult(list);
        }

        private IReadOnlyList<InstalledPackage> FilterForView(IEnumerable<InstalledPackage> snapshot, bool includeTransitive)
        {
            return snapshot
                .Where(p => includeTransitive || !p.Transitive || p.HasVulnerability == true)
                .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /* -------------------------- phase 2: enrichment ------------------------- */

        private Task EnsureEnrichmentAsync()
        {
            if (_enrichTask != null && _enrichToken == _runToken) return _enrichTask;
            var token = _runToken;
            _enrichToken = token;
            _enrichTask = RunEnrichmentSafeAsync(token);
            return _enrichTask;
        }

        private async Task RunEnrichmentSafeAsync(int token)
        {
            try
            {
                await RunEnrichmentAsync(token).ConfigureAwait(false);
            }
            catch
            {
                // enrichment is best-effort
            }
            finally
            {
                if (_enrichToken == token) _notify.Progress("", true);
            }
        }

        private async Task RunEnrichmentAsync(int token)
        {
            var snap = await EnsureSnapshotAsync().ConfigureAwait(false);
            if (token != _runToken) return;

            var direct = snap.Where(p => !p.Transitive).ToList();
            var includePrerelease = _config.GetBool(SettingKeys.DefaultIncludePrerelease, false);
            var minAgeDays = MinimumPackageAgeDays();

            if (_registries.GetEnabledRegistries().Count > 0 && direct.Count > 0)
            {
                var total = direct.Count;
                var done = 0;
                var lastPush = 0L;
                _notify.Progress($"Checking {total} package{(total == 1 ? "" : "s")} for updates…", false);
                await Concurrency.MapAsync(direct, 12, async (pkg, _) =>
                {
                    if (token != _runToken) return;
                    await EnrichPackageAsync(pkg, includePrerelease, minAgeDays).ConfigureAwait(false);
                    var d = Interlocked.Increment(ref done);
                    _notify.Progress($"Checking {total} packages for updates… ({d}/{total})", false);
                    var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (nowMs - Interlocked.Read(ref lastPush) > 400)
                    {
                        Interlocked.Exchange(ref lastPush, nowMs);
                        PushEnriched(EnrichPhase.Updates);
                    }
                }).ConfigureAwait(false);
                if (token != _runToken) return;
                PushEnriched(EnrichPhase.Updates);
            }

            _notify.Progress("Checking for known vulnerabilities…", false);
            await ApplyAuditsAsync(snap, token).ConfigureAwait(false);
            if (token != _runToken) return;
            PushEnriched(EnrichPhase.Vulnerabilities);

            if (UsePackageManagerForEnumeration())
            {
                await ReconcileWithNpmAsync(snap, token).ConfigureAwait(false);
                if (token != _runToken) return;
            }

            PushEnriched(EnrichPhase.Done);
        }

        private void PushEnriched(EnrichPhase phase)
        {
            if (_snapshot == null) return;
            _notify.Enriched(phase, FilterForView(_snapshot, _lastIncludeTransitive));
        }

        private async Task EnrichPackageAsync(InstalledPackage pkg, bool includePrerelease, int minAgeDays)
        {
            var registry = _registries.RegistryForPackage(pkg.Id);
            var candidates = registry != null
                ? new[] { registry }.Concat(_registries.GetEnabledRegistries().Where(r => r != registry)).ToList()
                : _registries.GetEnabledRegistries().ToList();

            foreach (var r in candidates)
            {
                try
                {
                    var doc = await _metadata.GetDocumentAsync(r.Url, pkg.Id).ConfigureAwait(false);
                    var versions = doc.Versions?.Keys.ToList() ?? new List<string>();
                    var latest = SemverUtil.MaxVersion(versions, includePrerelease)
                                 ?? (doc.DistTags.TryGetValue("latest", out var lt) ? lt : null);
                    if (!string.IsNullOrEmpty(latest))
                    {
                        pkg.LatestVersion = latest;
                        pkg.LatestPublished = doc.Time != null && doc.Time.TryGetValue(latest!, out var t) ? t : null;
                        if (!string.IsNullOrEmpty(pkg.LatestPublished) && minAgeDays > 0)
                        {
                            var ageDays = PackageAge.AgeInDays(pkg.LatestPublished);
                            pkg.LatestBelowMinAge = !double.IsInfinity(ageDays) && ageDays < minAgeDays;
                        }
                    }

                    var installedVersion = CleanVersion(pkg.ResolvedVersion ?? pkg.RequestedVersion ?? "");
                    if (installedVersion.Length > 0 &&
                        doc.Versions != null &&
                        doc.Versions.TryGetValue(installedVersion, out var manifest) &&
                        !string.IsNullOrEmpty(manifest.Deprecated))
                    {
                        pkg.Deprecated = true;
                    }

                    return;
                }
                catch
                {
                    // try the next registry
                }
            }
        }

        private async Task ApplyAuditsAsync(List<InstalledPackage> snap, int token)
        {
            var byId = snap.ToDictionary(p => p.Id.ToLowerInvariant(), p => p, StringComparer.Ordinal);
            var roots = new Dictionary<string, (string Dir, List<string> ProjectPaths)>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in _projects.GetProjects())
            {
                if (project.PackageManager != PackageManagerName.Npm) continue;
                var dir = LockGraph.FindLockfileRoot(project.Dir, project.WorkspaceRootDir)?.Dir;
                if (dir == null) continue;
                if (!roots.TryGetValue(dir, out var bucket))
                {
                    bucket = (dir, new List<string>());
                    roots[dir] = bucket;
                }

                bucket.ProjectPaths.Add(project.Info.Path);
            }

            if (roots.Count == 0 || !await _cli.IsAvailableAsync(PackageManagerName.Npm).ConfigureAwait(false)) return;

            foreach (var bucket in roots.Values)
            {
                if (token != _runToken) return;
                var result = await _cli.AuditAsync(bucket.Dir).ConfigureAwait(false);
                if (result?.Vulnerabilities == null) continue;
                foreach (var name in result.Vulnerabilities.Keys)
                {
                    if (!byId.TryGetValue(name.ToLowerInvariant(), out var entry)) continue;
                    var advisories = Advisories.CollectAdvisories(name, result.Vulnerabilities);
                    if (advisories.Count == 0) continue;
                    ApplyAdvisories(entry, advisories);
                    foreach (var p in bucket.ProjectPaths) MarkVulnerableProject(entry, p);
                }
            }
        }

        private async Task ReconcileWithNpmAsync(List<InstalledPackage> snap, int token)
        {
            if (!await _cli.IsAvailableAsync(PackageManagerName.Npm).ConfigureAwait(false)) return;
            var byId = snap.ToDictionary(p => p.Id.ToLowerInvariant(), p => p, StringComparer.Ordinal);
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in _projects.GetProjects())
            {
                if (project.PackageManager != PackageManagerName.Npm) continue;
                roots.Add(LockGraph.FindLockfileRoot(project.Dir, project.WorkspaceRootDir)?.Dir ?? project.Dir);
            }

            _notify.Progress("Reconciling with npm…", false);
            foreach (var root in roots)
            {
                if (token != _runToken) return;
                var outdated = await _cli.OutdatedAsync(root).ConfigureAwait(false);
                if (outdated == null) continue;
                foreach (var kv in outdated)
                {
                    if (byId.TryGetValue(kv.Key.ToLowerInvariant(), out var pkg) && !string.IsNullOrEmpty(kv.Value.Latest))
                    {
                        pkg.LatestVersion = kv.Value.Latest;
                    }
                }
            }
        }

        /* ------------------------------ folding -------------------------------- */

        private static void FoldProjectModel(WorkspaceProject project, Dictionary<string, InstalledPackage> merged)
        {
            foreach (var reference in project.Parsed.Dependencies)
            {
                var key = reference.Id.ToLowerInvariant();
                if (!merged.TryGetValue(key, out var entry))
                {
                    entry = new InstalledPackage
                    {
                        Id = reference.Id,
                        RequestedVersion = reference.Version,
                        Transitive = false,
                    };
                    merged[key] = entry;
                }

                entry.Transitive = false;
                if (!string.IsNullOrEmpty(reference.Version) && string.IsNullOrEmpty(entry.RequestedVersion))
                {
                    entry.RequestedVersion = reference.Version;
                }

                if (!entry.Projects.Contains(project.Info.Path)) entry.Projects.Add(project.Info.Path);
                if (!entry.ProjectVersions.Any(pv => pv.Project == project.Info.Path))
                {
                    entry.ProjectVersions.Add(new ProjectVersionRef
                    {
                        Project = project.Info.Path,
                        Version = reference.Version,
                        DependencyType = reference.DependencyType,
                    });
                }
            }
        }

        private static void MarkPinned(Dictionary<string, InstalledPackage> merged)
        {
            foreach (var entry in merged.Values)
            {
                foreach (var pv in entry.ProjectVersions) pv.Pinned = VersionRange.IsExactVersionPin(pv.Version);
                var direct = entry.ProjectVersions;
                entry.Pinned = direct.Count > 0 && direct.All(pv => pv.Pinned == true);
                if (entry.Pinned == true)
                {
                    var versions = new HashSet<string>(direct.Select(pv => VersionRange.StripVersionPin(pv.Version)));
                    entry.PinnedVersion = versions.Count == 1 ? versions.First() : null;
                }
            }
        }

        private static void MarkVulnerableProject(InstalledPackage entry, string projectPath)
        {
            entry.VulnerableProjects ??= new List<string>();
            if (!entry.VulnerableProjects.Contains(projectPath)) entry.VulnerableProjects.Add(projectPath);
            if (!entry.Projects.Contains(projectPath)) entry.Projects.Add(projectPath);
        }

        private static void ApplyAdvisories(InstalledPackage entry, IReadOnlyList<VulnerabilityInfo> advisories)
        {
            var list = entry.Vulnerabilities ?? new List<VulnerabilityInfo>();
            foreach (var a in advisories)
            {
                if (!list.Any(x => x.AdvisoryUrl == a.AdvisoryUrl && x.Title == a.Title))
                {
                    list.Add(a);
                }
            }

            list.Sort((x, y) => y.Severity.CompareTo(x.Severity));
            entry.Vulnerabilities = list;
            entry.HasVulnerability = true;
            entry.MaxVulnerabilitySeverity = list.Aggregate(-1, (m, a) => Math.Max(m, a.Severity));
        }

        private int MinimumPackageAgeDays()
        {
            var raw = _config.GetInt(SettingKeys.MinimumPackageAgeDays, 7);
            return raw > 0 ? raw : 0;
        }

        private bool UsePackageManagerForEnumeration() => _config.GetBool(SettingKeys.UsePackageManagerForEnumeration, false);

        private static void ApplyGraphEdges(InstalledPackage entry, string key, DependencyGraph graph)
        {
            if (graph.Dependents.TryGetValue(key, out var requiredBy) && requiredBy.Count > 0)
            {
                entry.RequiredBy = requiredBy
                    .Select(k => graph.DisplayName.TryGetValue(k, out var name) ? name : k)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (graph.Dependencies.TryGetValue(key, out var dependsOn) && dependsOn.Count > 0)
            {
                entry.DependsOn = dependsOn
                    .Select(k => graph.DisplayName.TryGetValue(k, out var name) ? name : k)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        private static string CleanVersion(string raw) => raw.TrimStart('^', '~', '=').Trim();

        public static string ProjectDisplayName(string projectPath) =>
            Path.GetFileName(Path.GetDirectoryName(projectPath) ?? "") is { Length: > 0 } name ? name : projectPath;
    }
}
