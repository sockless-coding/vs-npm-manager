using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio.Extensibility.UI;
using SocklessNpmManager.Core.Model;
using SocklessNpmManager.Core.Npm;

namespace SocklessNpmManager.Vs.ToolWindows
{
    /// <summary>The detail pane: version / save-as / add-as pickers, the per-project checklist, the
    /// install / update / uninstall / pin / unpin actions, advisories and readme.</summary>
    [DataContract]
    internal sealed class PackageDetailViewModel : NotifyPropertyChangedObject
    {
        private static readonly string[] SaveAsLabels =
        {
            "^ Compatible (minor + patch)", "~ Approximately (patch only)", "Exact version", ">= At least this version",
        };

        private static readonly VersionPrefix[] SaveAsValues =
        {
            VersionPrefix.Caret, VersionPrefix.Tilde, VersionPrefix.Exact, VersionPrefix.Gte,
        };

        private static readonly string[] AddAsLabels =
        {
            "Dependencies", "Dev Dependencies", "Peer Dependencies", "Optional Dependencies",
        };

        private static readonly DependencyType[] AddAsValues =
        {
            DependencyType.Dependencies, DependencyType.DevDependencies, DependencyType.PeerDependencies, DependencyType.OptionalDependencies,
        };

        private readonly PackageDetail _detail;
        private readonly InstalledPackage? _installed;
        private readonly Func<MutationRequest, Task<MutationResult>> _mutate;
        private readonly Action<string> _openUrl;
        private readonly Action<string> _selectPackage;
        private readonly bool _includePrerelease;
        private readonly int _minAgeDays;

        private string _selectedVersion;
        private string _selectedSaveAs;
        private string _selectedAddAs = "Dependencies";
        private bool _busy;
        private string _freshWarning = "";

        public PackageDetailViewModel(
            PackageDetail detail,
            IReadOnlyList<ProjectInfo> projects,
            InstalledPackage? installed,
            IReadOnlyList<string> preselectPaths,
            int minAgeDays,
            bool includePrerelease,
            Func<MutationRequest, Task<MutationResult>> mutate,
            Action<string> openUrl,
            Action<string> selectPackage)
        {
            _detail = detail;
            _installed = installed;
            _mutate = mutate;
            _openUrl = openUrl;
            _selectPackage = selectPackage;
            _includePrerelease = includePrerelease;
            _minAgeDays = minAgeDays;

            Id = detail.Id;
            Description = detail.Description;
            Subtitle = string.Join(" · ", new[]
            {
                detail.Authors.Count > 0 ? "by " + string.Join(", ", detail.Authors) : null,
                string.IsNullOrEmpty(detail.Source) ? null : detail.Source,
                string.IsNullOrEmpty(detail.LicenseExpression) ? null : detail.LicenseExpression,
            }.Where(s => s != null));
            ProjectUrl = detail.ProjectUrl ?? "";
            ReadmeText = detail.ReadmePlainText ?? detail.ReadmeMarkdown ?? "";

            var visible = detail.Versions.Where(v => includePrerelease || !v.IsPrerelease).ToList();
            Versions = visible.Select(v => v.Version).ToList();
            VersionOptions = visible.Select(v => new VersionOptionViewModel
            {
                Version = v.Version,
                Display = BuildVersionDisplay(v, minAgeDays),
            }).ToList();

            var installedVersion = installed != null
                ? VersionRange.StripVersionPin(installed.PinnedVersion
                    ?? installed.ProjectVersions.FirstOrDefault()?.Version
                    ?? installed.RequestedVersion
                    ?? installed.ResolvedVersion
                    ?? "")
                : "";
            _selectedVersion = !string.IsNullOrEmpty(installedVersion) && Versions.Contains(installedVersion)
                ? installedVersion
                : PackageAge.PickDefaultVersion(detail.Versions, includePrerelease, minAgeDays);

            _selectedSaveAs = LabelFor(installed != null
                ? VersionRange.DetectVersionPrefix(installed.ProjectVersions.FirstOrDefault()?.Version ?? installed.RequestedVersion)
                : VersionPrefix.Caret);

            var installedPaths = new HashSet<string>(installed?.Projects ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var vulnerablePaths = new HashSet<string>(
                installed?.VulnerableProjects?.Count > 0 ? installed.VulnerableProjects
                    : installed?.HasVulnerability == true ? installed.Projects
                    : Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            var scoped = preselectPaths.Where(p => projects.Any(x => x.Path == p)).ToList();
            var defaultChecked = scoped.Count > 0
                ? new HashSet<string>(scoped, StringComparer.OrdinalIgnoreCase)
                : installedPaths.Count > 0 ? installedPaths
                : new HashSet<string>(projects.Select(p => p.Path), StringComparer.OrdinalIgnoreCase);

            Projects = projects.Select(p =>
            {
                var pv = installed?.ProjectVersions.FirstOrDefault(x => x.Project == p.Path);
                var raw = pv?.Version ?? (installedPaths.Contains(p.Path) ? installed?.RequestedVersion ?? "—" : "—");
                var vm = new ProjectCheckViewModel
                {
                    Name = p.Name,
                    Path = p.Path,
                    PackageManager = p.PackageManager.ToCliName(),
                    InstalledVersion = raw == "—" ? "—" : VersionRange.StripVersionPin(raw),
                    Pinned = VersionRange.IsExactVersionPin(raw) || pv?.Pinned == true,
                    Vulnerable = vulnerablePaths.Contains(p.Path),
                    IsWorkspaceRoot = p.IsWorkspaceRoot,
                    DependencyTypeTag = pv?.DependencyType switch
                    {
                        DependencyType.DevDependencies => "dev",
                        DependencyType.PeerDependencies => "peer",
                        DependencyType.OptionalDependencies => "optional",
                        _ => "",
                    },
                    IsChecked = defaultChecked.Contains(p.Path),
                };
                vm.PropertyChanged += (_, __) => RecomputeActions();
                return vm;
            }).ToList();

            Callouts = BuildCallouts(minAgeDays);
            Advisories = (installed?.Vulnerabilities ?? detail.Vulnerabilities ?? new List<VulnerabilityInfo>())
                .Select(v => $"{Sev(v.Severity)}  {v.Title}{(string.IsNullOrEmpty(v.Range) ? "" : $"  (affects {v.Range})")}")
                .ToList();
            DependencyGroups = detail.DependencyGroups
                .Select(g => $"{g.Kind}: " + string.Join(", ", g.Dependencies.Select(d => $"{d.Id} {d.Range}")))
                .ToList();

            InstallCommand = new AsyncCommand((_, _, ct) => MutateAsync(InstallAction.Install, ct));
            UpdateCommand = new AsyncCommand((_, _, ct) => MutateAsync(InstallAction.Update, ct));
            UninstallCommand = new AsyncCommand((_, _, ct) => MutateAsync(InstallAction.Uninstall, ct));
            PinCommand = new AsyncCommand((_, _, ct) => MutateAsync(InstallAction.Pin, ct));
            UnpinCommand = new AsyncCommand((_, _, ct) => MutateAsync(InstallAction.Unpin, ct));
            OpenProjectUrlCommand = new AsyncCommand((_, _, _) => { if (!string.IsNullOrEmpty(ProjectUrl)) _openUrl(ProjectUrl); return Task.CompletedTask; });

            UpdateFreshWarning(minAgeDays);
            RecomputeActions();
        }

        [DataMember] public string Id { get; }
        [DataMember] public string Description { get; }
        [DataMember] public string Subtitle { get; }
        [DataMember] public string ProjectUrl { get; }
        [DataMember] public Visibility ProjectUrlVisibility => NpmManagerData.Vis(!string.IsNullOrEmpty(ProjectUrl));
        [DataMember] public Visibility DescriptionVisibility => NpmManagerData.Vis(!string.IsNullOrEmpty(Description));
        [DataMember] public string ReadmeText { get; }
        [DataMember] public Visibility ReadmeVisibility => NpmManagerData.Vis(!string.IsNullOrEmpty(ReadmeText));

        [DataMember] public IReadOnlyList<string> Versions { get; }
        [DataMember] public IReadOnlyList<VersionOptionViewModel> VersionOptions { get; }
        [DataMember] public IReadOnlyList<string> SaveAsOptions { get; } = SaveAsLabels;
        [DataMember] public IReadOnlyList<string> AddAsOptions { get; } = AddAsLabels;
        [DataMember] public IReadOnlyList<ProjectCheckViewModel> Projects { get; }
        [DataMember] public IReadOnlyList<string> Callouts { get; }
        [DataMember] public IReadOnlyList<string> Advisories { get; }
        [DataMember] public IReadOnlyList<string> DependencyGroups { get; }
        [DataMember] public Visibility CalloutsVisibility => NpmManagerData.Vis(Callouts.Count > 0);
        [DataMember] public Visibility AdvisoriesVisibility => NpmManagerData.Vis(Advisories.Count > 0);
        [DataMember] public Visibility DependencyGroupsVisibility => NpmManagerData.Vis(DependencyGroups.Count > 0);

        [DataMember]
        public string SelectedVersion
        {
            get => _selectedVersion;
            set { if (SetProperty(ref _selectedVersion, value)) { UpdateFreshWarning(_minAgeDays); RecomputeActions(); } }
        }

        [DataMember]
        public string SelectedSaveAs
        {
            get => _selectedSaveAs;
            set { if (SetProperty(ref _selectedSaveAs, value)) { RecomputeActions(); } }
        }

        [DataMember]
        public string SelectedAddAs
        {
            get => _selectedAddAs;
            set => SetProperty(ref _selectedAddAs, value);
        }

        [DataMember]
        public string FreshWarning
        {
            get => _freshWarning;
            private set { if (SetProperty(ref _freshWarning, value)) RaiseNotifyPropertyChangedEvent(nameof(FreshWarningVisibility)); }
        }

        [DataMember] public Visibility FreshWarningVisibility => NpmManagerData.Vis(!string.IsNullOrEmpty(_freshWarning));

        [DataMember]
        public bool Busy
        {
            get => _busy;
            private set
            {
                if (SetProperty(ref _busy, value))
                {
                    RecomputeActions();
                }
            }
        }

        private bool _canInstall, _canUpdate, _canUninstall, _canPin, _canUnpin;

        [DataMember] public bool CanInstall { get => _canInstall; private set { if (SetProperty(ref _canInstall, value)) { RaiseNotifyPropertyChangedEvent(nameof(SaveAsVisibility)); RaiseNotifyPropertyChangedEvent(nameof(AddAsVisibility)); } } }
        [DataMember] public bool CanUpdate { get => _canUpdate; private set { if (SetProperty(ref _canUpdate, value)) RaiseNotifyPropertyChangedEvent(nameof(SaveAsVisibility)); } }
        [DataMember] public bool CanUninstall { get => _canUninstall; private set => SetProperty(ref _canUninstall, value); }
        [DataMember] public bool CanPin { get => _canPin; private set => SetProperty(ref _canPin, value); }
        [DataMember] public bool CanUnpin { get => _canUnpin; private set => SetProperty(ref _canUnpin, value); }
        [DataMember] public Visibility SaveAsVisibility => NpmManagerData.Vis(_canInstall || _canUpdate);
        [DataMember] public Visibility AddAsVisibility => NpmManagerData.Vis(_canInstall);

        [DataMember] public IAsyncCommand InstallCommand { get; }
        [DataMember] public IAsyncCommand UpdateCommand { get; }
        [DataMember] public IAsyncCommand UninstallCommand { get; }
        [DataMember] public IAsyncCommand PinCommand { get; }
        [DataMember] public IAsyncCommand UnpinCommand { get; }
        [DataMember] public IAsyncCommand OpenProjectUrlCommand { get; }

        public event Action<string>? Toast;

        private IEnumerable<string> CheckedPaths => Projects.Where(p => p.IsChecked).Select(p => p.Path);

        private void RecomputeActions()
        {
            var chosen = CheckedPaths.ToList();
            var installedPaths = new HashSet<string>(_installed?.Projects ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var chosenInstalled = chosen.Where(installedPaths.Contains).ToList();
            var chosenNotInstalled = chosen.Where(p => !installedPaths.Contains(p)).ToList();
            var prefix = PrefixValue();

            bool ProjectPinned(string path) => _installed?.ProjectVersions.FirstOrDefault(pv => pv.Project == path)?.Pinned == true;
            var chosenPinned = chosenInstalled.Where(ProjectPinned).ToList();
            var chosenUnpinned = chosenInstalled.Where(p => !ProjectPinned(p)).ToList();

            CanInstall = !Busy && chosenNotInstalled.Count > 0;
            CanUpdate = !Busy && chosenInstalled.Count > 0 && chosenInstalled.Any(path =>
            {
                var raw = _installed?.ProjectVersions.FirstOrDefault(pv => pv.Project == path)?.Version ?? _installed?.RequestedVersion ?? "";
                return VersionRange.StripVersionPin(raw) != SelectedVersion || VersionRange.DetectVersionPrefix(raw) != prefix;
            });
            CanUninstall = !Busy && chosenInstalled.Count > 0;
            CanPin = !Busy && chosenUnpinned.Count > 0;
            CanUnpin = !Busy && chosenPinned.Count > 0;

            RaiseNotifyPropertyChangedEvent(nameof(SaveAsVisibility));
            RaiseNotifyPropertyChangedEvent(nameof(AddAsVisibility));
        }

        private async Task MutateAsync(InstallAction action, CancellationToken cancellationToken)
        {
            var chosen = CheckedPaths.ToList();
            if (chosen.Count == 0) return;

            var installedPaths = new HashSet<string>(_installed?.Projects ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var targets = action switch
            {
                InstallAction.Install => chosen.Where(p => !installedPaths.Contains(p)).ToList(),
                _ => chosen.Where(installedPaths.Contains).ToList(),
            };
            if (targets.Count == 0) targets = chosen;

            Busy = true;
            try
            {
                var result = await _mutate(new MutationRequest
                {
                    Action = action,
                    PackageId = _detail.Id,
                    Version = SelectedVersion,
                    ProjectPaths = targets,
                    Source = _detail.Source,
                    DependencyType = DependencyTypeValue(),
                    VersionPrefix = PrefixValue(),
                }).ConfigureAwait(false);

                var verb = action.ToString();
                if (result.Ok)
                {
                    Toast?.Invoke($"{verb} {result.PackageId}{(result.InstallNeeded ? " — run an install to finish" : "")}");
                }
                else
                {
                    var failed = result.PerProject.Where(p => !p.Ok).ToList();
                    Toast?.Invoke($"{verb} failed for {string.Join(", ", failed.Select(f => f.Project))}: {failed.FirstOrDefault()?.Message}");
                }
            }
            catch (Exception ex)
            {
                Toast?.Invoke(ex.Message);
            }
            finally
            {
                Busy = false;
            }
        }

        private VersionPrefix PrefixValue()
        {
            var idx = Array.IndexOf(SaveAsLabels, _selectedSaveAs);
            return idx >= 0 ? SaveAsValues[idx] : VersionPrefix.Caret;
        }

        private DependencyType DependencyTypeValue()
        {
            var idx = Array.IndexOf(AddAsLabels, _selectedAddAs);
            return idx >= 0 ? AddAsValues[idx] : DependencyType.Dependencies;
        }

        private static string LabelFor(VersionPrefix p) => SaveAsLabels[Array.IndexOf(SaveAsValues, p)];

        private void UpdateFreshWarning(int minAgeDays)
        {
            var info = _detail.Versions.FirstOrDefault(v => v.Version == SelectedVersion);
            if (minAgeDays > 0 && info != null && PackageAge.AgeInDays(info.Published) < minAgeDays)
            {
                FreshWarning = $"Version {SelectedVersion} was published {PackageAge.FormatRelativeAge(info.Published)} — within your {minAgeDays}-day minimum package age. New releases are the highest-risk window for a compromised package.";
            }
            else
            {
                FreshWarning = "";
            }
        }

        private List<string> BuildCallouts(int minAgeDays)
        {
            var list = new List<string>();
            if (_detail.Deprecation != null)
            {
                list.Add("Deprecated. " + (_detail.Deprecation.Message ?? string.Join(", ", _detail.Deprecation.Reasons)));
            }

            if (_installed?.Pinned == true && _installed.HasVulnerability == true)
            {
                list.Add($"Pinned & vulnerable. This package is pinned to {_installed.PinnedVersion ?? "an exact version"} and held back from Update All, but the pinned version has a known advisory. Choose a fixed version above and Update (it stays pinned), or Unpin.");
            }
            else if (_installed?.Pinned == true)
            {
                list.Add($"Pinned. Referenced as {_installed.PinnedVersion ?? "an exact version"} and held back from Update All. Unpin to allow updates; vulnerability checks still apply either way.");
            }

            return list;
        }

        private static string Sev(int s) => s switch { 3 => "CRITICAL", 2 => "HIGH", 1 => "MODERATE", 0 => "LOW", _ => "UNKNOWN" };

        private static string BuildVersionDisplay(VersionInfo v, int minAgeDays)
        {
            var parts = new List<string> { v.Version };
            if (v.IsPrerelease) parts.Add("prerelease");

            var published = TryFormatDate(v.Published);
            if (published != null) parts.Add(published);

            if (minAgeDays > 0 && PackageAge.AgeInDays(v.Published) < minAgeDays)
            {
                parts.Add("⚠ released " + PackageAge.FormatRelativeAge(v.Published));
            }

            return string.Join("   ·   ", parts);
        }

        private static string? TryFormatDate(string? iso)
        {
            return DateTimeOffset.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dto)
                ? dto.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                : null;
        }
    }

    [DataContract]
    internal sealed class VersionOptionViewModel
    {
        [DataMember] public string Version { get; set; } = "";
        [DataMember] public string Display { get; set; } = "";
    }
}
