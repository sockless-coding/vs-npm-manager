using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio.Extensibility.UI;
using SocklessNpmManager.Core.Hosting;
using SocklessNpmManager.Core.Model;
using SocklessNpmManager.Core.Npm;
using SocklessNpmManager.Vs.Hosting;

namespace SocklessNpmManager.Vs.ToolWindows
{
    /// <summary>
    /// Root data context for the tool window's Remote UI — the port of the VS Code webview's
    /// <c>App.tsx</c>: the Browse / Installed / Updates / Consolidate tabs, the toolbar, the package
    /// list and the detail pane.
    /// </summary>
    [DataContract]
    internal sealed class NpmManagerData : NotifyPropertyChangedObject, IDisposable
    {
        private const string AllRegistries = "All registries";
        private const int PageSize = 25;

        private readonly NpmManagerSession _session;
        private SynchronizationContext? _sync;
        private NpmManagerController? _controller;

        public void SetUiContext(SynchronizationContext? context) => _sync = context;

        private List<InstalledPackage> _installed = new();
        private IReadOnlyList<ProjectInfo> _projects = Array.Empty<ProjectInfo>();
        private List<PackageSummary> _searchResults = new();
        private bool _searchHasMore;
        private int _minAgeDays;
        private bool _packageManagerAvailable = true;
        private IReadOnlyList<string> _preselect = Array.Empty<string>();

        private string _selectedTab = "installed";

        internal static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
        private string _searchText = "";
        private bool _includePrerelease;
        private bool _includeTransitive;
        private string _selectedRegistry = AllRegistries;
        private string _status = "";
        private string _notice = "";
        private string _progress = "";
        private string _toast = "";
        private bool _busy;
        private PackageRowViewModel? _selectedRow;
        private PackageDetailViewModel? _detail;
        private Timer? _searchDebounce;
        private int _searchSeq;

        public NpmManagerData(NpmManagerSession session)
        {
            _session = session;

            RefreshCommand = new AsyncCommand((_, _, ct) => ReloadAsync(ct));
            SelectTabCommand = new AsyncCommand((p, _, ct) => SelectTabAsync(p as string, ct));
            UpdateAllCommand = new AsyncCommand((_, _, ct) => UpdateAllAsync(ct));
            LoadMoreCommand = new AsyncCommand((_, _, ct) => RunSearchAsync(append: true, ct));
            SearchNowCommand = new AsyncCommand((_, _, ct) => RunSearchAsync(append: false, ct));
            DismissToastCommand = new AsyncCommand((_, _, _) => { Toast = ""; return Task.CompletedTask; });

            _session.ScopeChanged += OnScopeChanged;
        }

        /* ------------------------------ toolbar ------------------------------- */

        [DataMember]
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    SelectedTab = "browse";
                    _searchDebounce?.Dispose();
                    _searchDebounce = new Timer(_ => Post(() => _ = RunSearchAsync(false, CancellationToken.None)), null, 300, Timeout.Infinite);
                }
            }
        }

        [DataMember]
        public bool IncludePrerelease
        {
            get => _includePrerelease;
            set
            {
                if (SetProperty(ref _includePrerelease, value))
                {
                    _ = RunSearchAsync(false, CancellationToken.None);
                    if (_selectedRow != null) _ = LoadDetailAsync(_selectedRow.Id, CancellationToken.None);
                }
            }
        }

        [DataMember]
        public bool IncludeTransitive
        {
            get => _includeTransitive;
            set { if (SetProperty(ref _includeTransitive, value)) _ = ReloadInstalledAsync(CancellationToken.None); }
        }

        [DataMember] public ObservableList<string> Registries { get; } = new();

        [DataMember]
        public string SelectedRegistry
        {
            get => _selectedRegistry;
            set
            {
                if (SetProperty(ref _selectedRegistry, value))
                {
                    _ = RunSearchAsync(false, CancellationToken.None);
                }
            }
        }

        /* ------------------------------- tabs -------------------------------- */

        [DataMember]
        public string SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (SetProperty(ref _selectedTab, value))
                {
                    foreach (var n in new[]
                    {
                        nameof(IsBrowse), nameof(IsInstalled), nameof(IsUpdates), nameof(IsConsolidate),
                        nameof(TransitiveToggleVisibility), nameof(UpdateAllVisibility), nameof(LoadMoreVisibility),
                    })
                    {
                        RaiseNotifyPropertyChangedEvent(n);
                    }

                    RebuildRows();
                }
            }
        }

        [DataMember] public bool IsBrowse => _selectedTab == "browse";
        [DataMember] public bool IsInstalled => _selectedTab == "installed";
        [DataMember] public bool IsUpdates => _selectedTab == "updates";
        [DataMember] public bool IsConsolidate => _selectedTab == "consolidate";
        [DataMember] public Visibility TransitiveToggleVisibility => Vis(IsInstalled);
        [DataMember] public Visibility UpdateAllVisibility => Vis(IsUpdates && UpdatesCount > 0);

        private int _installedCount, _updatesCount, _consolidateCount, _vulnerableCount;
        [DataMember] public int InstalledCount { get => _installedCount; private set { if (SetProperty(ref _installedCount, value)) RaiseNotifyPropertyChangedEvent(nameof(InstalledTabLabel)); } }
        [DataMember] public int UpdatesCount { get => _updatesCount; private set { if (SetProperty(ref _updatesCount, value)) { RaiseNotifyPropertyChangedEvent(nameof(UpdateAllVisibility)); RaiseNotifyPropertyChangedEvent(nameof(UpdatesTabLabel)); } } }
        [DataMember] public int ConsolidateCount { get => _consolidateCount; private set { if (SetProperty(ref _consolidateCount, value)) RaiseNotifyPropertyChangedEvent(nameof(ConsolidateTabLabel)); } }
        [DataMember] public int VulnerableCount { get => _vulnerableCount; private set => SetProperty(ref _vulnerableCount, value); }

        [DataMember] public string InstalledTabLabel => $"Installed ({_installedCount})";
        [DataMember] public string UpdatesTabLabel => $"Updates ({_updatesCount})";
        [DataMember] public string ConsolidateTabLabel => $"Consolidate ({_consolidateCount})";

        /* ------------------------------ content ------------------------------ */

        [DataMember] public ObservableList<PackageRowViewModel> Rows { get; } = new();

        [DataMember]
        public PackageRowViewModel? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (SetProperty(ref _selectedRow, value))
                {
                    RaiseNotifyPropertyChangedEvent(nameof(HasSelection));
                    if (value != null) _ = LoadDetailAsync(value.Id, CancellationToken.None);
                    else Detail = null;
                }
            }
        }

        [DataMember] public bool HasSelection => _selectedRow != null;

        [DataMember]
        public PackageDetailViewModel? Detail
        {
            get => _detail;
            private set
            {
                var old = _detail;
                if (SetProperty(ref _detail, value))
                {
                    if (old != null) old.Toast -= OnDetailToast;
                    if (value != null) value.Toast += OnDetailToast;
                    RaiseNotifyPropertyChangedEvent(nameof(DetailVisibility));
                    RaiseNotifyPropertyChangedEvent(nameof(NoDetailVisibility));
                }
            }
        }

        [DataMember] public Visibility DetailVisibility => Vis(_detail != null);
        [DataMember] public Visibility NoDetailVisibility => Vis(_detail == null);

        [DataMember] public Visibility LoadMoreVisibility => Vis(IsBrowse && _searchHasMore && !_busy);

        /* ------------------------------ status ------------------------------- */

        private string _statusBase = "";

        [DataMember] public string Status { get => _status; private set => SetProperty(ref _status, value); }

        /// <summary>Persistent warning (e.g. no package manager on PATH). Shown as plain text, no fill.</summary>
        [DataMember] public string Notice { get => _notice; private set { if (SetProperty(ref _notice, value)) RaiseNotifyPropertyChangedEvent(nameof(NoticeVisibility)); } }
        [DataMember] public Visibility NoticeVisibility => Vis(!string.IsNullOrEmpty(_notice));

        private void SetStatusBase(string text)
        {
            _statusBase = text;
            RecomputeStatus();
        }

        private void SetProgress(string text)
        {
            _progress = text;
            RecomputeStatus();
        }

        private void RecomputeStatus()
        {
            Status = string.IsNullOrEmpty(_progress) ? _statusBase : $"{_statusBase}   —   {_progress}";
        }
        [DataMember] public string Toast { get => _toast; private set { if (SetProperty(ref _toast, value)) RaiseNotifyPropertyChangedEvent(nameof(ToastVisibility)); } }
        [DataMember] public Visibility ToastVisibility => Vis(!string.IsNullOrEmpty(_toast));

        [DataMember]
        public bool Busy
        {
            get => _busy;
            private set
            {
                if (SetProperty(ref _busy, value))
                {
                    RaiseNotifyPropertyChangedEvent(nameof(NotBusy));
                    RaiseNotifyPropertyChangedEvent(nameof(LoadMoreVisibility));
                }
            }
        }

        [DataMember] public bool NotBusy => !_busy;

        /* ----------------------------- commands ----------------------------- */

        [DataMember] public IAsyncCommand RefreshCommand { get; }
        [DataMember] public IAsyncCommand SelectTabCommand { get; }
        [DataMember] public IAsyncCommand UpdateAllCommand { get; }
        [DataMember] public IAsyncCommand LoadMoreCommand { get; }
        [DataMember] public IAsyncCommand SearchNowCommand { get; }
        [DataMember] public IAsyncCommand DismissToastCommand { get; }

        /* ------------------------------ loading ----------------------------- */

        public async Task LoadAsync(CancellationToken cancellationToken)
        {
            Busy = true;
            try
            {
                var controller = await _session.EnsureInitializedAsync().ConfigureAwait(false);
                WireController(controller);

                var initial = await controller.GetInitialStateAsync().ConfigureAwait(false);
                _minAgeDays = initial.MinimumPackageAgeDays;
                _preselect = initial.PreselectProjectPaths;
                _includePrerelease = initial.DefaultIncludePrerelease;
                RaiseNotifyPropertyChangedEvent(nameof(IncludePrerelease));

                Registries.Clear();
                Registries.Add(AllRegistries);
                foreach (var r in initial.Registries.Where(r => r.Enabled))
                {
                    Registries.Add(r.RequiresAuth ? r.Name + "  🔒" : r.Name);
                }

                _projects = initial.Projects;
                await ReloadInstalledAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SetStatusBase("Failed to load: " + ex.Message);
            }
            finally
            {
                Busy = false;
            }
        }

        private async Task ReloadAsync(CancellationToken cancellationToken)
        {
            if (_controller != null) await _controller.RefreshAsync().ConfigureAwait(false);
            _projects = _controller?.ListProjects() ?? _projects;
            await ReloadInstalledAsync(cancellationToken).ConfigureAwait(false);
            if (IsBrowse) await RunSearchAsync(false, cancellationToken).ConfigureAwait(false);
        }

        private async Task ReloadInstalledAsync(CancellationToken cancellationToken)
        {
            if (_controller == null) return;
            try
            {
                SetStatusBase("Reading package.json files…");
                var (packages, pmAvailable) = await _controller.ListInstalledAsync(_includeTransitive).ConfigureAwait(false);
                _installed = packages.ToList();
                _packageManagerAvailable = pmAvailable;
                Notice = pmAvailable ? "" : "No npm / yarn / pnpm on PATH — changes are written directly to package.json; run an install afterwards.";
                RebuildCounts();
                RebuildRows();
            }
            catch (Exception ex)
            {
                SetStatusBase("Failed: " + ex.Message);
            }
        }

        private string Scope()
        {
            var s = _session.CurrentScope;
            return $"{s.Mode} · {s.Roots.Count} folder(s)";
        }

        private Task SelectTabAsync(string? tab, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(tab)) SelectedTab = tab!;
            return Task.CompletedTask;
        }

        private async Task RunSearchAsync(bool append, CancellationToken cancellationToken)
        {
            if (_controller == null) return;
            var seq = Interlocked.Increment(ref _searchSeq);
            var query = _searchText;
            var source = _selectedRegistry.Split(new[] { "  🔒" }, StringSplitOptions.None)[0];

            Busy = true;
            try
            {
                var skip = append ? _searchResults.Count : 0;
                var page = await _controller.SearchAsync(query, skip, PageSize, _includePrerelease, source, cancellationToken).ConfigureAwait(false);
                if (seq != _searchSeq) return;

                _searchResults = append ? _searchResults.Concat(page.Results).ToList() : page.Results.ToList();
                _searchHasMore = page.HasMore;
                if (IsBrowse) RebuildRows();
                RaiseNotifyPropertyChangedEvent(nameof(LoadMoreVisibility));
            }
            catch (Exception ex)
            {
                Toast = ex.Message;
            }
            finally
            {
                if (seq == _searchSeq) Busy = false;
            }
        }

        private async Task UpdateAllAsync(CancellationToken cancellationToken)
        {
            if (_controller == null) return;
            var updates = UpdatesList();
            var toUpdate = updates.Where(p => !string.IsNullOrEmpty(p.LatestVersion) && p.LatestBelowMinAge != true && p.Pinned != true).ToList();
            var heldAge = updates.Count(p => p.LatestBelowMinAge == true && p.Pinned != true);
            var heldPin = updates.Count(p => p.Pinned == true);

            Busy = true;
            var done = 0;
            try
            {
                foreach (var pkg in toUpdate)
                {
                    try
                    {
                        await _controller.MutateAsync(new MutationRequest
                        {
                            Action = InstallAction.Update,
                            PackageId = pkg.Id,
                            Version = pkg.LatestVersion,
                            ProjectPaths = pkg.Projects,
                            VersionPrefix = VersionRange.DetectVersionPrefix(pkg.ProjectVersions.FirstOrDefault()?.Version ?? pkg.RequestedVersion),
                        }).ConfigureAwait(false);
                        done++;
                    }
                    catch
                    {
                        // keep going
                    }
                }
            }
            finally
            {
                Busy = false;
            }

            var notes = new List<string>();
            if (heldAge > 0) notes.Add($"{heldAge} newer than the {_minAgeDays}-day minimum age");
            if (heldPin > 0) notes.Add($"{heldPin} pinned");
            Toast = $"Updated {done} package(s)" + (notes.Count > 0 ? " — held back: " + string.Join(", ", notes) : "");
            await ReloadInstalledAsync(cancellationToken).ConfigureAwait(false);
        }

        /* ------------------------------ detail ------------------------------ */

        private async Task LoadDetailAsync(string id, CancellationToken cancellationToken)
        {
            if (_controller == null) return;
            var source = _selectedRegistry.Split(new[] { "  🔒" }, StringSplitOptions.None)[0];
            try
            {
                var detail = await _controller.GetPackageDetailAsync(id, source, _includePrerelease, cancellationToken).ConfigureAwait(false);
                var installed = _installed.FirstOrDefault(p => string.Equals(p.Id, detail.Id, StringComparison.OrdinalIgnoreCase));
                Detail = new PackageDetailViewModel(
                    detail, _projects, installed, _preselect, _minAgeDays, _includePrerelease,
                    mutate: req => _controller!.MutateAsync(req),
                    openUrl: url => _ = _controller!.OpenExternalAsync(url),
                    selectPackage: pkgId =>
                    {
                        var row = Rows.FirstOrDefault(r => string.Equals(r.Id, pkgId, StringComparison.OrdinalIgnoreCase));
                        if (row != null) SelectedRow = row;
                    });
            }
            catch (Exception ex)
            {
                Toast = ex.Message;
            }
        }

        private void OnDetailToast(string message)
        {
            Toast = message;
            _ = ReloadInstalledAsync(CancellationToken.None);
        }

        /* ------------------------------- rows ------------------------------- */

        private void RebuildRows()
        {
            IEnumerable<PackageRowViewModel> next = _selectedTab switch
            {
                "browse" => _searchResults.Select(p => PackageRowViewModel.FromSummary(p, _minAgeDays)),
                "updates" => UpdatesList().Select(PackageRowViewModel.FromInstalled),
                "consolidate" => ConsolidateList().Select(PackageRowViewModel.FromInstalled),
                _ => InstalledList().Select(PackageRowViewModel.FromInstalled),
            };

            var list = next.ToList();
            Rows.Clear();
            foreach (var r in list) Rows.Add(r);

            SetStatusBase(_selectedTab switch
            {
                "browse" => string.IsNullOrWhiteSpace(_searchText) ? "Type to search the npm registry."
                    : list.Count == 0 ? "No packages match your search."
                    : $"{list.Count} result(s)",
                "updates" => list.Count == 0 ? "All packages are up to date." : $"{list.Count} update(s) available",
                "consolidate" => list.Count == 0 ? "All packages use a consistent version across package.json files." : $"{list.Count} inconsistent package(s)",
                _ => $"{Scope()} · {list.Count} package(s)",
            });
        }

        private void RebuildCounts()
        {
            InstalledCount = _installed.Count(p => !p.Transitive);
            UpdatesCount = UpdatesList().Count;
            ConsolidateCount = ConsolidateList().Count;
            VulnerableCount = _installed.Count(p => p.HasVulnerability == true);
        }

        private IEnumerable<InstalledPackage> InstalledList() =>
            _installed.Where(p => _includeTransitive || !p.Transitive || p.HasVulnerability == true);

        private List<InstalledPackage> UpdatesList() =>
            _installed.Where(p => !p.Transitive
                                  && !string.IsNullOrEmpty(p.LatestVersion)
                                  && p.LatestVersion != VersionRange.StripVersionPin(p.RequestedVersion)).ToList();

        private List<InstalledPackage> ConsolidateList() =>
            _installed.Where(p => !p.Transitive && p.ProjectVersions.Select(pv => pv.Version).Distinct().Count() > 1).ToList();

        /* ---------------------------- controller ---------------------------- */

        private void WireController(NpmManagerController controller)
        {
            if (ReferenceEquals(_controller, controller)) return;
            _controller = controller;
            controller.Progress += (msg, done) => Post(() => SetProgress(done ? "" : msg));
            controller.InstalledEnriched += (phase, packages) => Post(() =>
            {
                _installed = packages.ToList();
                RebuildCounts();
                RebuildRows();
            });
            controller.InstalledChanged += () => Post(() => _ = ReloadInstalledAsync(CancellationToken.None));
            controller.ProjectsChanged += () => Post(() => _projects = controller.ListProjects());
        }

        private void OnScopeChanged(object? sender, EventArgs e) => Post(() => _ = LoadAsync(CancellationToken.None));

        private void Post(Action action)
        {
            if (_sync != null) _sync.Post(_ => action(), null);
            else action();
        }

        public void Dispose()
        {
            _searchDebounce?.Dispose();
            _session.ScopeChanged -= OnScopeChanged;
        }
    }
}
