using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SocklessNpmManager.Core.Model;

namespace SocklessNpmManager.Core.Npm
{
    public sealed class SearchOptions
    {
        public string Query { get; set; } = "";
        public int Skip { get; set; }
        public int Take { get; set; } = 25;
        public bool IncludePrerelease { get; set; }
    }

    public sealed class SearchPage
    {
        public IReadOnlyList<PackageSummary> Results { get; set; } = Array.Empty<PackageSummary>();
        public bool HasMore { get; set; }
    }

    /// <summary>
    /// Package search via the npm registry search endpoint. Port of <c>src/npm/search.ts</c>.
    /// https://github.com/npm/registry/blob/main/docs/REGISTRY-API.md#get-v1search
    /// </summary>
    public sealed class SearchService
    {
        private const string NpmjsHost = "registry.npmjs.org";
        private static readonly TimeSpan DownloadsTimeout = TimeSpan.FromMilliseconds(1500);

        private readonly NpmHttpClient _http;

        public SearchService(NpmHttpClient http)
        {
            _http = http;
        }

        public async Task<SearchPage> SearchAsync(string registryUrl, string registryName, SearchOptions opts, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(opts.Query))
            {
                return new SearchPage();
            }

            var baseUrl = new Uri(new Uri(registryUrl), "-/v1/search");
            var queryString =
                $"text={Uri.EscapeDataString(opts.Query)}" +
                $"&size={opts.Take.ToString(CultureInfo.InvariantCulture)}" +
                $"&from={opts.Skip.ToString(CultureInfo.InvariantCulture)}";
            var searchUrl = baseUrl + "?" + queryString;

            var body = await _http.GetJsonAsync<SearchResponse>(searchUrl, TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);

            var objects = body.Objects ?? new List<SearchResultEntry>();
            var results = objects.Select(entry =>
            {
                var p = entry.Package ?? new SearchPackage();
                return new PackageSummary
                {
                    Id = p.Name ?? "",
                    Version = p.Version ?? "",
                    Description = p.Description ?? "",
                    Authors = NormalizeAuthors(p),
                    ProjectUrl = FirstNonEmpty(p.Links?.Homepage, p.Links?.Repository),
                    LicenseExpression = !string.IsNullOrEmpty(p.License) && p.License != "UNKNOWN" ? p.License : null,
                    Tags = p.Keywords,
                    Source = registryName,
                    LatestPublished = p.Date,
                };
            }).ToList();

            if (Uri.TryCreate(registryUrl, UriKind.Absolute, out var reg) && reg.Authority == NpmjsHost && results.Count > 0)
            {
                await WithTimeout(ApplyDownloadCountsAsync(results, cancellationToken), DownloadsTimeout).ConfigureAwait(false);
            }

            return new SearchPage
            {
                Results = results,
                HasMore = opts.Skip + objects.Count < (body.Total ?? 0),
            };
        }

        private async Task ApplyDownloadCountsAsync(List<PackageSummary> results, CancellationToken cancellationToken)
        {
            try
            {
                var names = string.Join(",", results.Select(r => Uri.EscapeDataString(r.Id)));
                var url = $"https://api.npmjs.org/downloads/point/last-month/{names}";

                if (results.Count == 1)
                {
                    var single = await _http.GetJsonAsync<DownloadsPoint>(url, TimeSpan.FromHours(1), cancellationToken).ConfigureAwait(false);
                    if (single.Downloads.HasValue) results[0].TotalDownloads = single.Downloads;
                    return;
                }

                var body = await _http.GetJsonAsync<Dictionary<string, DownloadsPoint?>>(url, TimeSpan.FromHours(1), cancellationToken).ConfigureAwait(false);
                foreach (var r in results)
                {
                    if (body.TryGetValue(r.Id, out var d) && d?.Downloads != null)
                    {
                        r.TotalDownloads = d.Downloads;
                    }
                }
            }
            catch
            {
                // best-effort only
            }
        }

        private static IReadOnlyList<string> NormalizeAuthors(SearchPackage p)
        {
            if (!string.IsNullOrEmpty(p.Author?.Name)) return new[] { p.Author!.Name! };
            if (!string.IsNullOrEmpty(p.Publisher?.Username)) return new[] { p.Publisher!.Username! };
            if (p.Maintainers is { Count: > 0 }) return p.Maintainers.Select(m => m.Username ?? "").Where(s => s.Length > 0).ToList();
            return Array.Empty<string>();
        }

        private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrEmpty(v));

        private static async Task WithTimeout(Task task, TimeSpan timeout)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed == task) await task.ConfigureAwait(false);
        }

        /// <summary>
        /// Merge results from multiple registries, keeping the first entry per id and preserving the
        /// relevance ordering of the first registry that returned it.
        /// </summary>
        public static IReadOnlyList<PackageSummary> MergeSearchResults(IEnumerable<IReadOnlyList<PackageSummary>> lists)
        {
            var byId = new Dictionary<string, PackageSummary>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var list in lists)
            {
                foreach (var pkg in list)
                {
                    var key = pkg.Id.ToLowerInvariant();
                    if (!byId.ContainsKey(key))
                    {
                        byId[key] = pkg;
                        order.Add(key);
                    }
                }
            }

            return order.Select(k => byId[k]).ToList();
        }

        private sealed class SearchResponse
        {
            [JsonPropertyName("total")] public int? Total { get; set; }
            [JsonPropertyName("objects")] public List<SearchResultEntry>? Objects { get; set; }
        }

        private sealed class SearchResultEntry
        {
            [JsonPropertyName("package")] public SearchPackage? Package { get; set; }
        }

        private sealed class SearchPackage
        {
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("version")] public string? Version { get; set; }
            [JsonPropertyName("description")] public string? Description { get; set; }
            [JsonPropertyName("keywords")] public List<string>? Keywords { get; set; }
            [JsonPropertyName("date")] public string? Date { get; set; }
            [JsonPropertyName("license")] public string? License { get; set; }
            [JsonPropertyName("links")] public SearchLinks? Links { get; set; }
            [JsonPropertyName("author")] public NamedField? Author { get; set; }
            [JsonPropertyName("publisher")] public UsernameField? Publisher { get; set; }
            [JsonPropertyName("maintainers")] public List<UsernameField>? Maintainers { get; set; }
        }

        private sealed class SearchLinks
        {
            [JsonPropertyName("npm")] public string? Npm { get; set; }
            [JsonPropertyName("homepage")] public string? Homepage { get; set; }
            [JsonPropertyName("repository")] public string? Repository { get; set; }
            [JsonPropertyName("bugs")] public string? Bugs { get; set; }
        }

        private sealed class NamedField
        {
            [JsonPropertyName("name")] public string? Name { get; set; }
        }

        private sealed class UsernameField
        {
            [JsonPropertyName("username")] public string? Username { get; set; }
        }

        private sealed class DownloadsPoint
        {
            [JsonPropertyName("downloads")] public long? Downloads { get; set; }
        }
    }
}
