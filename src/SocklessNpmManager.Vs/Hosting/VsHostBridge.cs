using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SocklessNpmManager.Core.Hosting;

namespace SocklessNpmManager.Vs.Hosting
{
    /// <summary>
    /// <see cref="IHostBridge"/> implemented over Visual Studio. Scope comes from whichever command
    /// opened the manager (see <see cref="NpmManagerSession"/> and <c>ScopeResolver</c>).
    /// </summary>
    internal sealed class VsHostBridge : IHostBridge
    {
        private readonly NpmManagerSession _session;

        public VsHostBridge(NpmManagerSession session)
        {
            _session = session;
            Config = new VsHostConfig();
            Secrets = new VsHostSecrets();
            Logger = new VsHostLogger();
            _session.ScopeChangedInternal += (_, _) => ScopeChanged?.Invoke(this, EventArgs.Empty);
        }

        public IHostConfig Config { get; }
        public IHostSecrets Secrets { get; }
        public IHostLogger Logger { get; }

        public event EventHandler? ScopeChanged;

        public HostScope GetScope() => _session.CurrentScope;

        public IDisposable WatchFiles(IEnumerable<string> globs, Action onChanged)
        {
            var roots = _session.CurrentScope.Roots;
            return new FileWatch(roots.Count > 0 ? roots : new[] { Cwd() }, onChanged);
        }

        public Task<string?> PromptAsync(string title, string prompt, bool password, CancellationToken cancellationToken = default)
        {
            // TODO: a RemoteUI credential dialog. Until then, .npmrc-based auth still works; an
            // interactive token entry is not yet offered in the VS host.
            return Task.FromResult<string?>(null);
        }

        public Task OpenExternalAsync(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // ignored
            }

            return Task.CompletedTask;
        }

        public string Cwd()
        {
            var roots = _session.CurrentScope.Roots;
            return roots.Count > 0 ? roots[0] : Environment.CurrentDirectory;
        }
    }
}
