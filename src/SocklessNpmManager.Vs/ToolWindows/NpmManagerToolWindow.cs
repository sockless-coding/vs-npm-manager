using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;
using SocklessNpmManager.Vs.Hosting;

namespace SocklessNpmManager.Vs.ToolWindows
{
    /// <summary>The npm Package Manager tool window.</summary>
    [VisualStudioContribution]
    internal sealed class NpmManagerToolWindow : ToolWindow
    {
        private readonly NpmManagerData _data;

        public NpmManagerToolWindow(NpmManagerSession session)
        {
            this.Title = "npm Package Manager";
            _data = new NpmManagerData(session);
        }

        public override ToolWindowConfiguration ToolWindowConfiguration => new()
        {
            Placement = ToolWindowPlacement.DocumentWell,
        };

        public override Task InitializeAsync(CancellationToken cancellationToken) => _data.LoadAsync(cancellationToken);

        public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IRemoteUserControl>(new NpmManagerToolWindowControl(_data));
        }
    }
}
