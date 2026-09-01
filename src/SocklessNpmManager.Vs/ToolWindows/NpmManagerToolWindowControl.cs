using Microsoft.VisualStudio.Extensibility.UI;

namespace SocklessNpmManager.Vs.ToolWindows
{
    /// <summary>Remote UI content for the npm Package Manager tool window.</summary>
    internal sealed class NpmManagerToolWindowControl : RemoteUserControl
    {
        public NpmManagerToolWindowControl(NpmManagerData dataContext)
            : base(dataContext)
        {
        }
    }
}
