using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SocklessNpmManager.Core.Npm
{
    /// <summary>Raised for a non-retryable, non-success HTTP response.</summary>
    public sealed class HttpError : Exception
    {
        public HttpError(string message, int status) : base(message) => Status = status;

        public int Status { get; }
    }

    /// <summary>Resolves an <c>Authorization</c> header value for a request URL, or <c>null</c>.</summary>
    public delegate Task<string?> AuthProvider(string url);

    /// <summary>
    /// Thin HTTP helper around <see cref="HttpClient"/>. Adds per-host auth headers, retry with
    /// backoff on 429/5xx, and a small TTL response cache for GET requests. Port of
    /// <c>src/npm/httpClient.ts</c>.
    /// </summary>
    public sealed class NpmHttpClient : IDisposable
    {
        private static readonly HttpClient Shared = CreateClient();

        private readonly AuthProvider _auth;
        private readonly object _cacheLock = new object();
        private readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);

        public NpmHttpClient(AuthProvider? auth = null)
        {
            _auth = auth ?? (_ => Task.FromResult<string?>(null));
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            client.DefaultRequestHeaders.Add("User-Agent", "SocklessNpmManager-VS");
            return client;
        }

        public void ClearCache()
        {
            lock (_cacheLock) _cache.Clear();
        }

        public async Task<T> GetJsonAsync<T>(string url, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            var ttlMs = ttl?.TotalMilliseconds ?? 0;
            if (ttlMs > 0)
            {
                lock (_cacheLock)
                {
                    if (_cache.TryGetValue(url, out var hit) && hit.Expires > DateTime.UtcNow)
                    {
                        return Deserialize<T>(hit.Body);
                    }
                }
            }

            var body = await RequestAsync(url, cancellationToken, 0).ConfigureAwait(false);
            if (ttlMs > 0)
            {
                lock (_cacheLock)
                {
                    _cache[url] = new CacheEntry { Expires = DateTime.UtcNow.AddMilliseconds(ttlMs), Body = body };
                }
            }

            return Deserialize<T>(body);
        }

        private static T Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
        }

        internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private async Task<string> RequestAsync(string url, CancellationToken cancellationToken, int attempt)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");
            var token = await _auth(url).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.TryAddWithoutValidation("Authorization", token);
            }

            HttpResponseMessage response;
            try
            {
                response = await Shared.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (attempt < 3 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(250 * (1 << attempt), cancellationToken).ConfigureAwait(false);
                return await RequestAsync(url, cancellationToken, attempt + 1).ConfigureAwait(false);
            }

            using (response)
            {
                var status = (int)response.StatusCode;

                if ((status == 429 || status >= 500) && attempt < 3)
                {
                    var wait = RetryAfterMs(response) ?? 400 * (1 << attempt);
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                    return await RequestAsync(url, cancellationToken, attempt + 1).ConfigureAwait(false);
                }

                if (status == 401 || status == 403)
                {
                    throw new HttpError($"Authentication required for {HostOf(url)}", status);
                }

                if (status == 404)
                {
                    throw new HttpError($"Not found: {url}", 404);
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpError($"GET {url} failed: {status} {response.ReasonPhrase}", status);
                }

                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        private static int? RetryAfterMs(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("retry-after", out var values))
            {
                foreach (var v in values)
                {
                    if (int.TryParse(v, out var seconds) && seconds > 0) return seconds * 1000;
                }
            }

            return null;
        }

        public static string HostOf(string url)
        {
            try
            {
                return new Uri(url).Authority;
            }
            catch
            {
                return url;
            }
        }

        public void Dispose()
        {
            // The shared HttpClient is process-lifetime; nothing instance-scoped to release.
        }

        private struct CacheEntry
        {
            public DateTime Expires;
            public string Body;
        }
    }
}
