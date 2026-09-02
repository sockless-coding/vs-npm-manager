using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility.UI;

namespace SocklessNpmManager.Vs.ToolWindows
{
    /// <summary>Remote UI content for the npm Package Manager tool window.</summary>
    internal sealed class NpmManagerToolWindowControl : RemoteUserControl
    {
        private readonly NpmManagerData _data;

        public NpmManagerToolWindowControl(NpmManagerData dataContext, SynchronizationContext? synchronizationContext)
            : base(dataContext, synchronizationContext)
        {
            _data = dataContext;
        }

        /// <summary>Called once the control's XAML has loaded in the Visual Studio process.</summary>
        public override async Task ControlLoadedAsync(CancellationToken cancellationToken)
        {
            _data.SetUiContext(SynchronizationContext.Current);
            await _data.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
