using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SocklessNpmManager.Core.Model;

namespace SocklessNpmManager.Core.Npm
{
    /// <summary>
    /// Package documents ("packuments") from the npm registry. A single GET returns every version's
    /// manifest, publish dates and the readme. Port of <c>src/npm/metadata.ts</c>.
    /// </summary>
    public sealed class MetadataService
    {
        private static readonly TimeSpan DocTtl = TimeSpan.FromMinutes(5);
        private readonly NpmHttpClient _http;

        public MetadataService(NpmHttpClient http)
        {
            _http = http;
        }

        public Task<Packument> GetDocumentAsync(string registryUrl, string packageId, CancellationToken cancellationToken = default)
        {
            var url = new Uri(new Uri(registryUrl), PackageUrlSegment(packageId)).ToString();
            return _http.GetJsonAsync<Packument>(url, DocTtl, cancellationToken);
        }

        /// <summary>All published versions, newest-first.</summary>
        public async Task<List<string>> ListVersionsAsync(string registryUrl, string packageId, CancellationToken cancellationToken = default)
        {
            try
            {
                var doc = await GetDocumentAsync(registryUrl, packageId, cancellationToken).ConfigureAwait(false);
                return SemverUtil.SortVersionsDescending(doc.Versions?.Keys ?? Enumerable.Empty<string>());
            }
            catch
            {
                return new List<string>();
            }
        }

        public async Task<PackageDetail> GetPackageDetailAsync(
            string registryUrl,
            string registryName,
            string packageId,
            bool includePrerelease,
            CancellationToken cancellationToken = default)
        {
            var doc = await GetDocumentAsync(registryUrl, packageId, cancellationToken).ConfigureAwait(false);
            var allVersions = SemverUtil.SortVersionsDescending(doc.Versions?.Keys ?? Enumerable.Empty<string>());

            var versions = allVersions.Select(v => new VersionInfo
            {
                Version = v,
                IsPrerelease = SemverUtil.IsPrerelease(v),
                Published = doc.Time != null && doc.Time.TryGetValue(v, out var t) ? t : null,
            }).ToList();

            doc.DistTags.TryGetValue("latest", out var latestTag);
            var selectable = versions.Where(v => includePrerelease || !v.IsPrerelease).ToList();
            var selectedVersion =
                (!string.IsNullOrEmpty(latestTag) && selectable.Any(v => v.Version == latestTag) ? latestTag : selectable.FirstOrDefault()?.Version)
                ?? versions.FirstOrDefault()?.Version
                ?? "";

            VersionManifest? manifest = null;
            if (doc.Versions != null && selectedVersion.Length > 0)
            {
                doc.Versions.TryGetValue(selectedVersion, out manifest);
            }

            return new PackageDetail
            {
                Id = string.IsNullOrEmpty(doc.Name) ? packageId : doc.Name!,
                Versions = versions,
                SelectedVersion = selectedVersion,
                Description = manifest?.Description ?? "",
                Authors = NormalizeAuthors(manifest),
                ProjectUrl = FirstNonEmpty(manifest?.Homepage, doc.Homepage, RepositoryUrl(manifest?.Repository)),
                LicenseExpression = LicenseString(manifest?.License) ?? LicenseString(doc.License),
                Tags = manifest?.Keywords ?? new List<string>(),
                DependencyGroups = MapDependencyGroups(manifest),
                Deprecation = string.IsNullOrEmpty(manifest?.Deprecated)
                    ? null
                    : new DeprecationInfo { Reasons = new[] { manifest!.Deprecated! }, Message = manifest.Deprecated },
                ReadmeMarkdown = TrimReadme(doc.Readme),
                ReadmePlainText = ReadmeText.ToPlainText(TrimReadme(doc.Readme)),
                Source = registryName,
            };
        }

        private static IReadOnlyList<PackageDependencyGroup> MapDependencyGroups(VersionManifest? manifest)
        {
            if (manifest == null) return Array.Empty<PackageDependencyGroup>();
            var groups = new List<PackageDependencyGroup>();

            void Push(string kind, Dictionary<string, string>? deps)
            {
                if (deps == null || deps.Count == 0) return;
                groups.Add(new PackageDependencyGroup
                {
                    Kind = kind,
                    Dependencies = deps.Select(kv => new PackageDependency { Id = kv.Key, Range = kv.Value }).ToList(),
                });
            }

            Push("dependencies", manifest.Dependencies);
            Push("peerDependencies", manifest.PeerDependencies);
            Push("optionalDependencies", manifest.OptionalDependencies);
            return groups;
        }

        private static IReadOnlyList<string> NormalizeAuthors(VersionManifest? manifest)
        {
            if (manifest == null) return Array.Empty<string>();
            var author = ReadNamedField(manifest.Author);
            if (author != null) return new[] { author };
            if (manifest.Maintainers is { Count: > 0 })
            {
                return manifest.Maintainers
                    .Select(m => m.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => s!)
                    .ToList();
            }

            return Array.Empty<string>();
        }

        private static string? ReadNamedField(JsonElement? element)
        {
            if (element == null || element.Value.ValueKind == JsonValueKind.Undefined || element.Value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            var e = element.Value;
            if (e.ValueKind == JsonValueKind.String) return e.GetString();
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                return name.GetString();
            }

            return null;
        }

        private static string? LicenseString(JsonElement? element)
        {
            if (element == null) return null;
            var e = element.Value;
            if (e.ValueKind == JsonValueKind.String)
            {
                var s = e.GetString();
                return s == "UNKNOWN" ? null : s;
            }

            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
            {
                return type.GetString();
            }

            return null;
        }

        private static string? RepositoryUrl(JsonElement? element)
        {
            if (element == null) return null;
            var e = element.Value;
            string? raw = e.ValueKind == JsonValueKind.String
                ? e.GetString()
                : e.ValueKind == JsonValueKind.Object && e.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
                    ? u.GetString()
                    : null;

            if (string.IsNullOrEmpty(raw)) return null;

            return Regex.Replace(raw, "^git\\+", "")
                .Replace(".git", "")
                .Replace("git://", "https://");
        }

        private static string? TrimReadme(string? readme)
        {
            if (string.IsNullOrEmpty(readme)) return null;
            if (Regex.IsMatch(readme!.Trim(), "no readme data", RegexOptions.IgnoreCase)) return null;
            return readme!.Length > 20_000 ? readme.Substring(0, 20_000) + "\n\n…" : readme;
        }

        private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrEmpty(v));

        /// <summary>npm percent-encodes the <c>/</c> in a scoped name but keeps the scope literal: <c>@types/node</c> → <c>@types%2fnode</c>.</summary>
        private static string PackageUrlSegment(string id)
        {
            if (id.StartsWith("@"))
            {
                var slash = id.IndexOf('/');
                if (slash > 0)
                {
                    return $"{id.Substring(0, slash)}%2f{Uri.EscapeDataString(id.Substring(slash + 1))}";
                }
            }

            return Uri.EscapeDataString(id);
        }
    }

    public sealed class Packument
    {
        [JsonPropertyName("name")] public string? Name { get; set; }

        [JsonPropertyName("dist-tags")] public Dictionary<string, string> DistTags { get; set; } = new Dictionary<string, string>();

        [JsonPropertyName("versions")] public Dictionary<string, VersionManifest>? Versions { get; set; }

        [JsonPropertyName("time")] public Dictionary<string, string>? Time { get; set; }

        [JsonPropertyName("readme")] public string? Readme { get; set; }

        [JsonPropertyName("license")] public JsonElement? License { get; set; }

        [JsonPropertyName("homepage")] public string? Homepage { get; set; }
    }

    public sealed class VersionManifest
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("author")] public JsonElement? Author { get; set; }
        [JsonPropertyName("license")] public JsonElement? License { get; set; }
        [JsonPropertyName("homepage")] public string? Homepage { get; set; }
        [JsonPropertyName("repository")] public JsonElement? Repository { get; set; }
        [JsonPropertyName("keywords")] public List<string>? Keywords { get; set; }
        [JsonPropertyName("dependencies")] public Dictionary<string, string>? Dependencies { get; set; }
        [JsonPropertyName("peerDependencies")] public Dictionary<string, string>? PeerDependencies { get; set; }
        [JsonPropertyName("optionalDependencies")] public Dictionary<string, string>? OptionalDependencies { get; set; }

        [JsonPropertyName("deprecated")]
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Deprecated { get; set; }

        [JsonPropertyName("maintainers")] public List<JsonElement>? Maintainers { get; set; }
    }

    /// <summary><c>deprecated</c> is usually a string but occasionally <c>true</c>; coerce either way.</summary>
    internal sealed class FlexibleStringConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String: return reader.GetString();
                case JsonTokenType.True: return "This package is deprecated";
                case JsonTokenType.False: return null;
                default:
                    reader.Skip();
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value == null) writer.WriteNullValue();
            else writer.WriteStringValue(value);
        }
    }
}
