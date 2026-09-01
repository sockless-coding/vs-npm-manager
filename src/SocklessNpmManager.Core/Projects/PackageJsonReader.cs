using System.Collections.Generic;
using System.Text.Json;
using SocklessNpmManager.Core.Model;

namespace SocklessNpmManager.Core.Projects
{
    public sealed class PackageDependencyRef
    {
        public string Id { get; set; } = "";

        /// <summary>Range/version as written (e.g. <c>^1.2.3</c>, <c>1.2.3</c>, <c>workspace:*</c>).</summary>
        public string Version { get; set; } = "";

        public DependencyType DependencyType { get; set; }
    }

    public sealed class ParsedPackageJson
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public List<PackageDependencyRef> Dependencies { get; } = new List<PackageDependencyRef>();

        /// <summary>Glob patterns for member packages, when this file is a workspace root.</summary>
        public List<string>? Workspaces { get; set; }
    }

    /// <summary>
    /// Reading <c>package.json</c>: the four dependency sections plus an optional <c>workspaces</c>
    /// field. Writing is done by <see cref="PackageJsonEditor"/> so formatting is preserved.
    /// Port of <c>src/projects/packageJson.ts</c>.
    /// </summary>
    public static class PackageJsonReader
    {
        private static readonly DependencyType[] DependencyFields =
        {
            DependencyType.Dependencies,
            DependencyType.DevDependencies,
            DependencyType.PeerDependencies,
            DependencyType.OptionalDependencies,
        };

        public static ParsedPackageJson Parse(string text)
        {
            var result = new ParsedPackageJson();

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            }
            catch
            {
                return result;
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return result;

                if (root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                {
                    result.Name = name.GetString();
                }

                if (root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String)
                {
                    result.Version = version.GetString();
                }

                foreach (var field in DependencyFields)
                {
                    if (!root.TryGetProperty(field.ToJsonKey(), out var section) || section.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var entry in section.EnumerateObject())
                    {
                        if (entry.Value.ValueKind == JsonValueKind.String)
                        {
                            result.Dependencies.Add(new PackageDependencyRef
                            {
                                Id = entry.Name,
                                Version = entry.Value.GetString() ?? "",
                                DependencyType = field,
                            });
                        }
                    }
                }

                if (root.TryGetProperty("workspaces", out var ws))
                {
                    if (ws.ValueKind == JsonValueKind.Array)
                    {
                        result.Workspaces = CollectStrings(ws);
                    }
                    else if (ws.ValueKind == JsonValueKind.Object &&
                             ws.TryGetProperty("packages", out var packages) &&
                             packages.ValueKind == JsonValueKind.Array)
                    {
                        result.Workspaces = CollectStrings(packages);
                    }
                }
            }

            return result;
        }

        private static List<string> CollectStrings(JsonElement array)
        {
            var list = new List<string>();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (s != null) list.Add(s);
                }
            }

            return list;
        }
    }
}
