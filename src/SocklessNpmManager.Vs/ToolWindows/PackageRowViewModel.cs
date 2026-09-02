using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;
using SocklessNpmManager.Core.Model;

namespace SocklessNpmManager.Vs.ToolWindows
{
    /// <summary>One row in the package list (Browse results or an Installed / Updates / Consolidate entry).</summary>
    [DataContract]
    internal sealed class PackageRowViewModel : NotifyPropertyChangedObject
    {
        private string _title = "";
        private string _description = "";
        private string _rightLabel = "";
        private string _badges = "";
        private bool _isSelected;

        [DataMember] public string Id { get; set; } = "";

        [DataMember]
        public string Title { get => _title; set => SetProperty(ref _title, value); }

        [DataMember]
        public string Description { get => _description; set => SetProperty(ref _description, value); }

        [DataMember]
        public string RightLabel { get => _rightLabel; set => SetProperty(ref _rightLabel, value); }

        [DataMember]
        public string Badges { get => _badges; set => SetProperty(ref _badges, value); }

        [DataMember]
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

        public static PackageRowViewModel FromSummary(PackageSummary p, int minAgeDays)
        {
            var badges = new List<string>();
            if (minAgeDays > 0 && Core.Npm.PackageAge.AgeInDays(p.LatestPublished) < minAgeDays) badges.Add("just released");
            return new PackageRowViewModel
            {
                Id = p.Id,
                Title = p.Id,
                Description = string.IsNullOrEmpty(p.Description) ? (p.Authors.Any() ? "by " + string.Join(", ", p.Authors) : "") : p.Description,
                RightLabel = p.Version,
                Badges = string.Join("  •  ", badges),
            };
        }

        public static PackageRowViewModel FromInstalled(InstalledPackage p)
        {
            var row = new PackageRowViewModel { Id = p.Id };
            row.UpdateFromInstalled(p);
            return row;
        }

        public void UpdateFromInstalled(InstalledPackage p)
        {
            Title = p.Id;

            var current = p.PinnedVersion ?? (string.IsNullOrEmpty(p.RequestedVersion) ? p.ResolvedVersion : p.RequestedVersion) ?? "?";
            RightLabel = !string.IsNullOrEmpty(p.LatestVersion) && p.LatestVersion != current && current != "?"
                ? $"{current} → {p.LatestVersion}"
                : current == "?" ? "" : current;

            Description = p.Transitive
                ? "Transitive dependency"
                : $"{p.Projects.Count} package.json" + (p.Projects.Count == 1 ? "" : "s");

            var badges = new List<string>();
            if (p.HasVulnerability == true) badges.Add("vulnerable");
            if (p.Deprecated == true) badges.Add("deprecated");
            if (p.Pinned == true) badges.Add("pinned");
            if (p.LatestBelowMinAge == true) badges.Add("just released");
            Badges = string.Join("  •  ", badges);
        }
    }
}
