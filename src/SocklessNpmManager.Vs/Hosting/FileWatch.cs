using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SocklessNpmManager.Vs.Hosting
{
    /// <summary>
    /// Watches <c>package.json</c> / lockfiles under a set of roots and raises a single debounced
    /// callback. Replaces the VS Code <c>FileSystemWatcher</c> glob used by
    /// <c>src/projects/discovery.ts</c>.
    /// </summary>
    internal sealed class FileWatch : IDisposable
    {
        private static readonly string[] WatchedNames =
        {
            "package.json", "package-lock.json", "yarn.lock", "pnpm-lock.yaml",
        };

        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly Action _onChanged;
        private readonly Timer _debounce;
        private int _disposed;

        public FileWatch(IEnumerable<string> roots, Action onChanged)
        {
            _onChanged = onChanged;
            _debounce = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);

            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                watcher.Changed += OnFsEvent;
                watcher.Created += OnFsEvent;
                watcher.Deleted += OnFsEvent;
                watcher.Renamed += OnFsEvent;
                _watchers.Add(watcher);
            }
        }

        private void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            var name = Path.GetFileName(e.Name ?? e.FullPath);
            if (Array.IndexOf(WatchedNames, name) < 0) return;
            if (e.FullPath.Replace('\\', '/').Contains("/node_modules/")) return;
            _debounce.Change(400, Timeout.Infinite);
        }

        private void Fire()
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            try
            {
                _onChanged();
            }
            catch
            {
                // ignored — the next event will retry
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _debounce.Dispose();
            foreach (var w in _watchers)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }

            _watchers.Clear();
        }
    }
}
