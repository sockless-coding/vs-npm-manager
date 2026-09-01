using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility.UI;
using SocklessNpmManager.Core.Hosting;
using SocklessNpmManager.Core.Model;
using SocklessNpmManager.Vs.Hosting;

namespace SocklessNpmManager.Vs.ToolWindows
{
    /// <summary>
    /// Data context for the tool window's Remote UI. This first iteration shows the Installed view
    /// only; the full Browse / Updates / Consolidate tabs and the detail pane follow.
    /// </summary>
    [DataContract]
    internal sealed class NpmManagerData : NotifyPropertyChangedObject
    {
        private readonly NpmManagerSession _session;
        private string _status = "Loading…";
        private string _scope = "";
        private bool _busy;
        private NpmManagerController? _wiredController;

        public NpmManagerData(NpmManagerSession session)
        {
            _session = session;
            RefreshCommand = new AsyncCommand((_, _, ct) => LoadAsync(ct));
            _session.ScopeChanged += OnScopeChanged;
        }

        [DataMember]
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        [DataMember]
        public string Scope
        {
            get => _scope;
            set => SetProperty(ref _scope, value);
        }

        [DataMember]
        public bool Busy
        {
            get => _busy;
            set
            {
                if (SetProperty(ref _busy, value))
                {
                    RaiseNotifyPropertyChangedEvent(nameof(NotBusy));
                }
            }
        }

        [DataMember]
        public bool NotBusy => !_busy;

        [DataMember]
        public ObservableList<NpmPackageRow> Packages { get; } = new();

        [DataMember]
        public IAsyncCommand RefreshCommand { get; }

        private void OnScopeChanged(object? sender, EventArgs e) => _ = LoadAsync(CancellationToken.None);

        public async Task LoadAsync(CancellationToken cancellationToken)
        {
            if (Busy) return;
            Busy = true;
            try
            {
                var controller = await _session.EnsureInitializedAsync().ConfigureAwait(false);
                WireEnrichment(controller);

                Scope = $"{_session.CurrentScope.Mode} · {_session.CurrentScope.Roots.Count} folder(s)";
                Status = "Reading package.json files…";

                var (packages, pmAvailable) = await controller.ListInstalledAsync(includeTransitive: false).ConfigureAwait(false);

                Packages.Clear();
                foreach (var p in packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
                {
                    Packages.Add(NpmPackageRow.From(p));
                }

                Status = pmAvailable
                    ? $"{Packages.Count} package(s)"
                    : $"{Packages.Count} package(s) — no npm/yarn/pnpm on PATH; changes edit package.json directly";
            }
            catch (Exception ex)
            {
                Status = "Failed to load: " + ex.Message;
            }
            finally
            {
                Busy = false;
            }
        }

        private void WireEnrichment(NpmManagerController controller)
        {
            if (ReferenceEquals(_wiredController, controller)) return;
            _wiredController = controller;
            controller.InstalledEnriched += OnEnriched;
            controller.InstalledChanged += () => _ = LoadAsync(CancellationToken.None);
        }

        private void OnEnriched(EnrichPhase phase, IReadOnlyList<InstalledPackage> packages)
        {
            var byId = packages.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                               .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var row in Packages)
            {
                if (byId.TryGetValue(row.Id, out var updated)) row.Update(updated);
            }

            if (phase == EnrichPhase.Done && !Busy) Status = $"{Packages.Count} package(s)";
        }
    }

    [DataContract]
    internal sealed class NpmPackageRow : NotifyPropertyChangedObject
    {
        private string _versionLabel = "";
        private string _badges = "";

        [DataMember] public string Id { get; set; } = "";
        [DataMember] public string Name { get; set; } = "";

        [DataMember]
        public string VersionLabel
        {
            get => _versionLabel;
            set => SetProperty(ref _versionLabel, value);
        }

        [DataMember]
        public string Badges
        {
            get => _badges;
            set => SetProperty(ref _badges, value);
        }

        public static NpmPackageRow From(InstalledPackage p)
        {
            var row = new NpmPackageRow { Id = p.Id, Name = p.Id };
            row.Update(p);
            return row;
        }

        public void Update(InstalledPackage p)
        {
            var current = p.PinnedVersion ?? p.RequestedVersion ?? p.ResolvedVersion ?? "?";
            VersionLabel = !string.IsNullOrEmpty(p.LatestVersion) && p.LatestVersion != current
                ? $"{current} → {p.LatestVersion}"
                : current;

            var badges = new List<string>();
            if (p.Transitive) badges.Add("transitive");
            if (p.Pinned == true) badges.Add("pinned");
            if (p.Deprecated == true) badges.Add("deprecated");
            if (p.HasVulnerability == true) badges.Add("vulnerable");
            if (p.LatestBelowMinAge == true) badges.Add("just released");
            Badges = string.Join("  •  ", badges);
        }
    }
}
