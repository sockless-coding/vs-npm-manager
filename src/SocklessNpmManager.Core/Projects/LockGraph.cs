using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace SocklessNpmManager.Core.Projects
{
    public enum LockfileKind
    {
        Npm,
        Yarn,
        Pnpm,
    }

    public sealed class LockfileRoot
    {
        public string Dir { get; set; } = "";
        public LockfileKind Kind { get; set; }
    }

    /// <summary>
    /// The resolved dependency graph for a workspace root. Port of <c>src/projects/lockGraph.ts</c>.
    /// Only <c>package-lock.json</c> (npm v2/v3) is parsed in full; <c>yarn.lock</c> / <c>pnpm-lock.yaml</c>
    /// repos fall back to a shallow <c>node_modules</c> scan.
    /// </summary>
    public sealed class DependencyGraph
    {
        /// <summary>idLower → ids it depends on directly (idLower).</summary>
        public Dictionary<string, HashSet<string>> Dependencies { get; } = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        /// <summary>idLower → ids that depend on it directly (idLower).</summary>
        public Dictionary<string, HashSet<string>> Dependents { get; } = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        /// <summary>idLower → resolved version.</summary>
        public Dictionary<string, string> Resolved { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>idLower → original casing.</summary>
        public Dictionary<string, string> DisplayName { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        internal void Note(string id)
        {
            var key = id.ToLowerInvariant();
            if (!DisplayName.ContainsKey(key)) DisplayName[key] = id;
        }

        internal void AddEdge(string parent, string child)
        {
            var p = parent.ToLowerInvariant();
            var c = child.ToLowerInvariant();
            if (p == c) return;
            GetOrAdd(Dependencies, p).Add(c);
            GetOrAdd(Dependents, c).Add(p);
        }

        internal static HashSet<string> GetOrAdd(Dictionary<string, HashSet<string>> map, string key)
        {
            if (!map.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                map[key] = set;
            }

            return set;
        }
    }

    public static class LockGraph
    {
        private static readonly string[] LockfileNames = { "package-lock.json", "yarn.lock", "pnpm-lock.yaml" };

        /// <summary><c>node_modules/foo</c> → <c>foo</c>; <c>node_modules/@scope/foo</c> → <c>@scope/foo</c>.</summary>
        private static string? NameFromPackagePath(string pkgPath)
        {
            var segments = pkgPath.Split(new[] { "node_modules/" }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) return null;
            var last = segments[segments.Length - 1];
            return last.Length == 0 ? null : last.TrimEnd('/');
        }

        /// <summary>Parse an npm <c>package-lock.json</c> (lockfileVersion 2 or 3, using the flat <c>packages</c> map).</summary>
        public static DependencyGraph BuildGraphFromNpmLockfile(JObject? lockJson)
        {
            var graph = new DependencyGraph();
            if (!(lockJson?["packages"] is JObject packages)) return graph;

            foreach (var prop in packages.Properties())
            {
                var pkgPath = prop.Name;
                if (pkgPath.Length == 0 || !(prop.Value is JObject entry)) continue;
                var name = NameFromPackagePath(pkgPath);
                if (name == null) continue;

                graph.Note(name);
                var key = name.ToLowerInvariant();
                var version = (string?)entry["version"];
                if (!string.IsNullOrEmpty(version) && !graph.Resolved.ContainsKey(key))
                {
                    graph.Resolved[key] = version!;
                }

                foreach (var childId in DependencyNames(entry))
                {
                    graph.Note(childId);
                    graph.AddEdge(name, childId);
                }
            }

            return graph;
        }

        private static IEnumerable<string> DependencyNames(JObject entry)
        {
            foreach (var field in new[] { "dependencies", "peerDependencies", "optionalDependencies" })
            {
                if (entry[field] is JObject deps)
                {
                    foreach (var d in deps.Properties()) yield return d.Name;
                }
            }
        }

        /// <summary>Shallow <c>node_modules</c> scan: top-level packages only.</summary>
        public static DependencyGraph BuildGraphFromNodeModules(string rootDir)
        {
            var graph = new DependencyGraph();
            var nodeModules = Path.Combine(rootDir, "node_modules");

            foreach (var name in ListPackageNames(nodeModules))
            {
                var pkgJsonPath = Path.Combine(nodeModules, Path.Combine(name.Split('/')), "package.json");
                JObject manifest;
                try
                {
                    manifest = JObject.Parse(File.ReadAllText(pkgJsonPath));
                }
                catch
                {
                    continue;
                }

                graph.Note(name);
                var key = name.ToLowerInvariant();
                var version = (string?)manifest["version"];
                if (!string.IsNullOrEmpty(version)) graph.Resolved[key] = version!;

                foreach (var childId in DependencyNames(manifest))
                {
                    graph.Note(childId);
                    graph.AddEdge(name, childId);
                }
            }

            return graph;
        }

        private static IEnumerable<string> ListPackageNames(string nodeModulesDir)
        {
            string[] entries;
            try
            {
                entries = Directory.GetDirectories(nodeModulesDir);
            }
            catch
            {
                yield break;
            }

            foreach (var dir in entries)
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.Length == 0 || dirName[0] == '.') continue;
                if (dirName[0] == '@')
                {
                    string[] scoped;
                    try
                    {
                        scoped = Directory.GetDirectories(dir);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var s in scoped)
                    {
                        yield return dirName + "/" + Path.GetFileName(s);
                    }
                }
                else
                {
                    yield return dirName;
                }
            }
        }

        /// <summary>Directory (at or above <paramref name="startDir"/>, not above <paramref name="stopDir"/>) containing a lockfile.</summary>
        public static LockfileRoot? FindLockfileRoot(string startDir, string stopDir)
        {
            var dir = startDir;
            while (true)
            {
                if (File.Exists(Path.Combine(dir, "package-lock.json"))) return new LockfileRoot { Dir = dir, Kind = LockfileKind.Npm };
                if (File.Exists(Path.Combine(dir, "yarn.lock"))) return new LockfileRoot { Dir = dir, Kind = LockfileKind.Yarn };
                if (File.Exists(Path.Combine(dir, "pnpm-lock.yaml"))) return new LockfileRoot { Dir = dir, Kind = LockfileKind.Pnpm };
                var parent = Path.GetDirectoryName(dir);
                if (dir == stopDir || string.IsNullOrEmpty(parent) || parent == dir) break;
                dir = parent;
            }

            return null;
        }

        /// <summary>Best-effort resolved graph for a project: its workspace's lockfile, or a <c>node_modules</c> scan.</summary>
        public static DependencyGraph ReadDependencyGraph(string projectDir, string workspaceRootDir)
        {
            var lockRoot = FindLockfileRoot(projectDir, workspaceRootDir);
            if (lockRoot?.Kind == LockfileKind.Npm)
            {
                try
                {
                    var raw = File.ReadAllText(Path.Combine(lockRoot.Dir, "package-lock.json"));
                    return BuildGraphFromNpmLockfile(JObject.Parse(raw));
                }
                catch
                {
                    // fall through to node_modules scan
                }
            }

            var scanRoot = lockRoot?.Dir ?? workspaceRootDir;
            return BuildGraphFromNodeModules(scanRoot);
        }

        public static DependencyGraph MergeGraphs(IEnumerable<DependencyGraph> graphs)
        {
            var merged = new DependencyGraph();
            foreach (var g in graphs)
            {
                foreach (var kv in g.DisplayName)
                {
                    if (!merged.DisplayName.ContainsKey(kv.Key)) merged.DisplayName[kv.Key] = kv.Value;
                }

                foreach (var kv in g.Resolved)
                {
                    if (!merged.Resolved.ContainsKey(kv.Key)) merged.Resolved[kv.Key] = kv.Value;
                }

                foreach (var kv in g.Dependencies)
                {
                    var into = DependencyGraph.GetOrAdd(merged.Dependencies, kv.Key);
                    foreach (var c in kv.Value) into.Add(c);
                }

                foreach (var kv in g.Dependents)
                {
                    var into = DependencyGraph.GetOrAdd(merged.Dependents, kv.Key);
                    foreach (var c in kv.Value) into.Add(c);
                }
            }

            return merged;
        }
    }
}
