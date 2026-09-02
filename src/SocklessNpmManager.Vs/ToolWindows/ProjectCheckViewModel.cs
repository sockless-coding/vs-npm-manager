using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;

namespace SocklessNpmManager.Vs.ToolWindows
{
    /// <summary>A selectable <c>package.json</c> in the detail pane's project checklist.</summary>
    [DataContract]
    internal sealed class ProjectCheckViewModel : NotifyPropertyChangedObject
    {
        private bool _isChecked;

        [DataMember] public string Name { get; set; } = "";
        [DataMember] public string Path { get; set; } = "";
        [DataMember] public string PackageManager { get; set; } = "";
        [DataMember] public string InstalledVersion { get; set; } = "—";
        [DataMember] public bool Pinned { get; set; }
        [DataMember] public bool Vulnerable { get; set; }
        [DataMember] public bool IsWorkspaceRoot { get; set; }
        [DataMember] public string DependencyTypeTag { get; set; } = "";

        [DataMember]
        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }
    }
}
