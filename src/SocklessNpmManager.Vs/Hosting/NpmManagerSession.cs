using System;
using System.Threading;
using System.Threading.Tasks;
using SocklessNpmManager.Core.Hosting;

namespace SocklessNpmManager.Vs.Hosting
{
    /// <summary>
    /// Process-lifetime singleton that owns the one <see cref="NpmManagerController"/> and the scope
    /// the manager is currently showing. Commands call <see cref="SetScopeAsync"/> to (re)target it;
    /// the tool window's view-models subscribe to the controller's events.
    /// </summary>
    internal sealed class NpmManagerSession
    {
        private readonly SemaphoreSlim _initGate = new(1, 1);
        private readonly VsHostBridge _bridge;
        private NpmManagerController? _controller;

        public NpmManagerSession()
        {
            _bridge = new VsHostBridge(this);
        }

        public HostScope CurrentScope { get; private set; } = HostScope.Empty;

        /// <summary>Raised (on the bridge) when the scope changes so Core can reload.</summary>
        internal event EventHandler? ScopeChangedInternal;

        /// <summary>Raised for the tool window so it can rebind after a scope change.</summary>
        public event EventHandler? ScopeChanged;

        public NpmManagerController Controller =>
            _controller ?? throw new InvalidOperationException("Call EnsureInitializedAsync first.");

        public async Task<NpmManagerController> EnsureInitializedAsync()
        {
            if (_controller != null) return _controller;
            await _initGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_controller == null)
                {
                    var controller = new NpmManagerController(_bridge);
                    await controller.InitializeAsync().ConfigureAwait(false);
                    _controller = controller;
                }
            }
            finally
            {
                _initGate.Release();
            }

            return _controller;
        }

        public async Task SetScopeAsync(HostScope scope)
        {
            CurrentScope = scope;
            await EnsureInitializedAsync().ConfigureAwait(false);
            ScopeChangedInternal?.Invoke(this, EventArgs.Empty);
            ScopeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
