using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SocklessNpmManager.Core.Cli;
using SocklessNpmManager.Core.Hosting;
using SocklessNpmManager.Core.Model;

namespace SocklessNpmManager.Core.Projects
{
    public sealed class WorkspaceProject
    {
        public ProjectInfo Info { get; set; } = new ProjectInfo();
        public ParsedPackageJson Parsed { get; set; } = new ParsedPackageJson();
        public string Dir { get; set; } = "";

        /// <summary>The containing scope root (or this project's own dir, for a loose file).</summary>
        public string WorkspaceRootDir { get; set; } = "";

        public PackageManagerName PackageManager { get; set; } = PackageManagerName.Npm;
    }

    /// <summary>
    /// Discovers <c>package.json</c> files under the current scope and keeps a live model of them,
    /// including npm/Yarn/pnpm workspace membership. Port of <c>src/projects/discovery.ts</c>, with
    /// <c>vscode.workspace.findFiles</c> replaced by a filesystem walk.
    /// </summary>
    public sealed class ProjectRegistry : IDisposable
    {
        private static readonly string[] PruneDirs = { "node_modules", ".git", ".hg", ".svn" };

        private readonly IHostBridge _host;
        private List<WorkspaceProject> _projects = new List<WorkspaceProject>();
        private IDisposable? _watch;

        public ProjectRegistry(IHostBridge host)
        {
            _host = host;
        }

        public event EventHandler? DidChange;

        public void Start()
        {
            _watch = _host.WatchFiles(
                new[] { "**/package.json", "**/package-lock.json", "**/yarn.lock", "**/pnpm-lock.yaml" },
                () => _ = RefreshAsync());
        }

        public IReadOnlyList<WorkspaceProject> GetProjects() => _projects;

        public WorkspaceProject? FindByPath(string projectPath)
        {
            var norm = NormalizePath(projectPath);
            return _projects.FirstOrDefault(p => NormalizePath(p.Info.Path) == norm);
        }

        /// <summary>
        /// Given the scope the manager was opened with, return the set of <c>package.json</c> paths it
        /// governs: for a project scope, that project plus every workspace member when it is a
        /// workspace root; empty otherwise ("no specific scope").
        /// </summary>
        public IReadOnlyList<string> ResolveSelectionScope(HostScope scope)
        {
            if (scope.Mode != ScopeMode.Project || scope.Roots.Count == 0)
            {
                return Array.Empty<string>();
            }

            var target = scope.Roots[0];
            var project = _projects.FirstOrDefault(p => NormalizePath(p.Dir) == NormalizePath(target))
                          ?? _projects.FirstOrDefault(p => NormalizePath(Path.GetDirectoryName(p.Info.Path) ?? "") == NormalizePath(target));
            if (project == null) return Array.Empty<string>();

            if (project.Info.IsWorkspaceRoot)
            {
                var members = _projects
                    .Where(p => p.Info.WorkspaceRoot != null && NormalizePath(p.Info.WorkspaceRoot) == NormalizePath(project.Info.Path))
                    .Select(p => p.Info.Path);
                return new[] { project.Info.Path }.Concat(members).ToList();
            }

            return new[] { project.Info.Path };
        }

        public Task RefreshAsync()
        {
            var roots = _host.GetScope().Roots;
            var searchRoots = roots.Count > 0 ? roots.ToList() : new List<string> { _host.Cwd() };

            var entries = new List<Entry>();
            foreach (var root in searchRoots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var filePath in EnumeratePackageJson(root))
                {
                    string text;
                    try
                    {
                        text = File.ReadAllText(filePath);
                    }
                    catch
                    {
                        continue;
                    }

                    var parsed = PackageJsonReader.Parse(text);
                    entries.Add(new Entry
                    {
                        FilePath = filePath,
                        Dir = Path.GetDirectoryName(filePath) ?? filePath,
                        Parsed = parsed,
                        WorkspaceRootDir = root,
                    });
                }
            }

            // De-dupe (scope roots can overlap).
            entries = entries
                .GroupBy(e => NormalizePath(e.FilePath))
                .Select(g => g.First())
                .ToList();

            var workspaceRoots = entries.Where(e => e.Parsed.Workspaces is { Count: > 0 }).ToList();
            var memberToRoot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var wsRoot in workspaceRoots)
            {
                foreach (var pattern in wsRoot.Parsed.Workspaces!)
                {
                    var memberGlob = JoinGlob(pattern, "package.json");
                    foreach (var candidate in entries)
                    {
                        if (NormalizePath(candidate.FilePath) == NormalizePath(wsRoot.FilePath)) continue;
                        var rel = MakeRelative(wsRoot.Dir, candidate.FilePath);
                        if (rel != null && GlobMatcher.IsMatch(memberGlob, rel))
                        {
                            memberToRoot[NormalizePath(candidate.FilePath)] = wsRoot.FilePath;
                        }
                    }
                }
            }

            var next = entries.Select(e =>
            {
                memberToRoot.TryGetValue(NormalizePath(e.FilePath), out var workspaceRoot);
                var isWorkspaceRoot = e.Parsed.Workspaces is { Count: > 0 };
                var pm = PackageManagerCli.DetectPackageManager(e.Dir, e.WorkspaceRootDir);
                return new WorkspaceProject
                {
                    Info = new ProjectInfo
                    {
                        Path = e.FilePath,
                        Name = string.IsNullOrEmpty(e.Parsed.Name) ? Path.GetFileName(e.Dir) : e.Parsed.Name!,
                        WorkspaceRoot = workspaceRoot,
                        PackageManager = pm,
                        IsWorkspaceRoot = isWorkspaceRoot,
                    },
                    Parsed = e.Parsed,
                    Dir = e.Dir,
                    WorkspaceRootDir = e.WorkspaceRootDir,
                    PackageManager = pm,
                };
            }).OrderBy(p => p.Info.Name, StringComparer.OrdinalIgnoreCase).ToList();

            _projects = next;
            DidChange?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        private static IEnumerable<string> EnumeratePackageJson(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();

                string? pkgJson = Path.Combine(dir, "package.json");
                if (File.Exists(pkgJson)) yield return pkgJson;

                string[] subDirs;
                try
                {
                    subDirs = Directory.GetDirectories(dir);
                }
                catch
                {
                    continue;
                }

                foreach (var sub in subDirs)
                {
                    var name = Path.GetFileName(sub);
                    if (name.Length == 0 || name[0] == '.' || Array.IndexOf(PruneDirs, name) >= 0)
                    {
                        continue;
                    }

                    stack.Push(sub);
                }
            }
        }

        private static string JoinGlob(string pattern, string filename)
        {
            var trimmed = pattern.TrimEnd('/');
            return $"{trimmed}/{filename}";
        }

        private static string? MakeRelative(string baseDir, string fullPath)
        {
            var b = NormalizePath(baseDir).TrimEnd('/') + "/";
            var f = NormalizePath(fullPath);
            return f.StartsWith(b, StringComparison.OrdinalIgnoreCase) ? f.Substring(b.Length) : null;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var full = path;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                // keep as-is
            }

            return full.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        }

        public void Dispose()
        {
            _watch?.Dispose();
            _watch = null;
        }

        private sealed class Entry
        {
            public string FilePath = "";
            public string Dir = "";
            public ParsedPackageJson Parsed = new ParsedPackageJson();
            public string WorkspaceRootDir = "";
        }
    }
}
