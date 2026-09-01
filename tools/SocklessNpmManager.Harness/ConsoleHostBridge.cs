using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SocklessNpmManager.Core.Hosting;
using SocklessNpmManager.Core.Model;

namespace SocklessNpmManager.Harness
{
    /// <summary>A trivial <see cref="IHostBridge"/> for exercising Core outside Visual Studio.</summary>
    internal sealed class ConsoleHostBridge : IHostBridge
    {
        private readonly string _root;

        public ConsoleHostBridge(string root, ScopeMode mode)
        {
            _root = root;
            GetScopeResult = new HostScope { Mode = mode, Roots = new[] { root } };
            Config = new ConsoleConfig();
            Secrets = new InMemorySecrets();
            Logger = new ConsoleLogger();
        }

        public IHostConfig Config { get; }
        public IHostSecrets Secrets { get; }
        public IHostLogger Logger { get; }
        public HostScope GetScopeResult { get; set; }

        public event EventHandler? ScopeChanged;

        public HostScope GetScope() => GetScopeResult;

        public IDisposable WatchFiles(IEnumerable<string> globs, Action onChanged) => new NoopDisposable();

        public Task<string?> PromptAsync(string title, string prompt, bool password, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"[prompt] {title}: {prompt}");
            return Task.FromResult<string?>(null);
        }

        public Task OpenExternalAsync(string url)
        {
            Console.WriteLine($"[open] {url}");
            return Task.CompletedTask;
        }

        public string Cwd() => _root;

        public void RaiseScopeChanged() => ScopeChanged?.Invoke(this, EventArgs.Empty);

        private sealed class ConsoleConfig : IHostConfig
        {
            public bool GetBool(string key, bool fallback) => fallback;
            public int GetInt(string key, int fallback) => fallback;
            public string GetString(string key, string fallback) => fallback;
            public IReadOnlyList<AdditionalRegistry> GetAdditionalRegistries() => Array.Empty<AdditionalRegistry>();
            public event EventHandler? ConfigChanged { add { } remove { } }
        }

        private sealed class InMemorySecrets : IHostSecrets
        {
            private readonly Dictionary<string, string> _store = new();
            public Task<string?> GetAsync(string key) => Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);
            public Task StoreAsync(string key, string value) { _store[key] = value; return Task.CompletedTask; }
            public Task DeleteAsync(string key) { _store.Remove(key); return Task.CompletedTask; }
        }

        private sealed class ConsoleLogger : IHostLogger
        {
            public void Line(string message) => Console.WriteLine(message);
            public void Append(string message) => Console.Write(message);
        }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
