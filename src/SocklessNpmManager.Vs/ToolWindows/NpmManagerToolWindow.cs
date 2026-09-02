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
        private readonly SynchronizationContext? _ctorContext;

        public NpmManagerToolWindow()
        {
            this.Title = "npm Package Manager";
            _ctorContext = SynchronizationContext.Current;
            _data = new NpmManagerData(NpmManagerSession.Shared);
        }

        public override ToolWindowConfiguration ToolWindowConfiguration => new()
        {
            Placement = ToolWindowPlacement.DocumentWell,
        };

        public override Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IRemoteUserControl>(
                new NpmManagerToolWindowControl(_data, _ctorContext ?? SynchronizationContext.Current));
        }
    }
}
