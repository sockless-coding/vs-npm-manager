using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SocklessNpmManager.Core.Hosting;

namespace SocklessNpmManager.Core.Npm
{
    public sealed class Registry
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public string? Scope { get; set; }
        public bool HasAuth { get; set; }
    }

    /// <summary>
    /// Registry discovery and authentication. Port of <c>src/npm/registries.ts</c>.
    ///
    /// Sources, in increasing priority: global npm config, user-level <c>~/.npmrc</c>, every
    /// <c>.npmrc</c> found walking up from each scope root, then <c>npmManager.additionalRegistries</c>.
    /// When a registry needs auth we use, in order: a token/password from <c>.npmrc</c>, a token
    /// previously saved in the host secret store, or an interactive prompt (stored for next time).
    /// </summary>
    public sealed class RegistryService
    {
        public const string SecretPrefix = "npmManager.registryToken:";
        private const string DefaultRegistry = "https://registry.npmjs.org/";

        private readonly IHostBridge _host;
        private List<Registry> _registries = new List<Registry>();
        private ParsedNpmrc _merged = new ParsedNpmrc();

        public RegistryService(IHostBridge host)
        {
            _host = host;
        }

        public IReadOnlyList<Registry> GetRegistries() => _registries;

        public IReadOnlyList<Registry> GetEnabledRegistries() => _registries.Where(r => r.Enabled).ToList();

        public Registry? FindByName(string name) => _registries.FirstOrDefault(r => r.Name == name);

        /// <summary>The registry to use for a (possibly scoped) package name.</summary>
        public Registry? RegistryForPackage(string packageId)
        {
            var scope = packageId.StartsWith("@") ? packageId.Split('/')[0].Substring(1) : null;
            if (scope != null)
            {
                var scoped = _registries.FirstOrDefault(r => r.Scope == scope && r.Enabled);
                if (scoped != null) return scoped;
            }

            return _registries.FirstOrDefault(r => r.Scope == null && r.Enabled) ?? _registries.FirstOrDefault();
        }

        public void Refresh()
        {
            var configs = CollectConfigFiles().Select(SafeParse).ToList();
            _merged = NpmrcParser.Merge(configs);

            var registries = new List<Registry>();
            var defaultUrl = Normalize(string.IsNullOrEmpty(_merged.Registry) ? DefaultRegistry : _merged.Registry!);
            registries.Add(new Registry
            {
                Name = NpmHttpClient.HostOf(defaultUrl),
                Url = defaultUrl,
                Enabled = true,
                HasAuth = HasAuthFor(_merged, defaultUrl),
            });

            foreach (var kv in _merged.ScopedRegistries)
            {
                var norm = Normalize(kv.Value);
                registries.Add(new Registry
                {
                    Name = "@" + kv.Key,
                    Url = norm,
                    Enabled = true,
                    Scope = kv.Key,
                    HasAuth = HasAuthFor(_merged, norm),
                });
            }

            foreach (var a in _host.Config.GetAdditionalRegistries())
            {
                if (string.IsNullOrWhiteSpace(a.Url)) continue;
                var norm = Normalize(a.Url);
                if (registries.Any(r => r.Url == norm)) continue;
                registries.Add(new Registry
                {
                    Name = string.IsNullOrEmpty(a.Name) ? NpmHttpClient.HostOf(norm) : a.Name,
                    Url = norm,
                    Enabled = true,
                    HasAuth = HasAuthFor(_merged, norm),
                });
            }

            _registries = registries;
        }

        /// <summary>An <c>Authorization</c> header value for a request URL, or <c>null</c>.</summary>
        public async Task<string?> GetAuthHeaderAsync(string requestUrl)
        {
            var tokenKey = NpmrcParser.FindAuthPrefix(_merged.AuthTokens.Keys, requestUrl);
            if (tokenKey != null) return "Bearer " + _merged.AuthTokens[tokenKey];

            var authKey = NpmrcParser.FindAuthPrefix(_merged.BasicAuth.Keys, requestUrl);
            if (authKey != null) return "Basic " + _merged.BasicAuth[authKey];

            var userPassKey = NpmrcParser.FindAuthPrefix(_merged.UserPass.Keys, requestUrl);
            if (userPassKey != null)
            {
                var up = _merged.UserPass[userPassKey];
                if (!string.IsNullOrEmpty(up.Username) && !string.IsNullOrEmpty(up.Password))
                {
                    var raw = Encoding.UTF8.GetBytes($"{up.Username}:{up.Password}");
                    return "Basic " + Convert.ToBase64String(raw);
                }
            }

            var registry = _registries.FirstOrDefault(r => NpmHttpClient.HostOf(r.Url) == NpmHttpClient.HostOf(requestUrl));
            if (registry == null) return null;

            var saved = await _host.Secrets.GetAsync(SecretPrefix + registry.Name).ConfigureAwait(false);
            return string.IsNullOrEmpty(saved) ? null : "Bearer " + saved;
        }

        /// <summary>Prompt for a token and persist it for the registry. Returns true if saved.</summary>
        public async Task<bool> PromptForCredentialsAsync(string registryName)
        {
            var registry = FindByName(registryName);
            if (registry == null) return false;

            var token = await _host.PromptAsync(
                $"Credentials for {registry.Name}",
                $"Enter an access token for {NpmHttpClient.HostOf(registry.Url)}",
                password: true).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token)) return false;

            await _host.Secrets.StoreAsync(SecretPrefix + registry.Name, token!).ConfigureAwait(false);
            return true;
        }

        public Task ClearCredentialsAsync(string registryName) =>
            _host.Secrets.DeleteAsync(SecretPrefix + registryName);

        private static bool HasAuthFor(ParsedNpmrc merged, string url) =>
            NpmrcParser.FindAuthPrefix(merged.AuthTokens.Keys, url) != null ||
            NpmrcParser.FindAuthPrefix(merged.BasicAuth.Keys, url) != null ||
            NpmrcParser.FindAuthPrefix(merged.UserPass.Keys, url) != null;

        private static string Normalize(string url)
        {
            var trimmed = url.Trim();
            return trimmed.EndsWith("/") ? trimmed : trimmed + "/";
        }

        private static ParsedNpmrc SafeParse(string file)
        {
            try
            {
                return NpmrcParser.Parse(File.ReadAllText(file));
            }
            catch
            {
                return new ParsedNpmrc();
            }
        }

        /// <summary>Ordered lowest → highest priority.</summary>
        private List<string> CollectConfigFiles()
        {
            var files = new List<string>();
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var isWindows = Path.DirectorySeparatorChar == '\\';
            if (isWindows)
            {
                var appData = Environment.GetEnvironmentVariable("APPDATA")
                              ?? Path.Combine(home, "AppData", "Roaming");
                AddIfFile(files, Path.Combine(appData, "npm", "etc", "npmrc"));
            }
            else
            {
                AddIfFile(files, "/etc/npmrc");
                AddIfFile(files, "/usr/local/etc/npmrc");
            }

            AddIfFile(files, Path.Combine(home, ".npmrc"));

            foreach (var root in ScopeRoots())
            {
                var chain = new List<string>();
                var dir = root;
                string? prev = null;
                while (!string.IsNullOrEmpty(dir) && dir != prev)
                {
                    var p = Path.Combine(dir, ".npmrc");
                    if (File.Exists(p)) chain.Add(p);
                    prev = dir;
                    dir = Path.GetDirectoryName(dir);
                }

                // chain is nearest-first; reverse so nearest ends up last (highest priority).
                chain.Reverse();
                files.AddRange(chain);
            }

            return Dedupe(files, isWindows);
        }

        private IEnumerable<string> ScopeRoots()
        {
            var roots = _host.GetScope().Roots;
            if (roots.Count > 0)
            {
                foreach (var r in roots) yield return r;
            }
            else
            {
                yield return _host.Cwd();
            }
        }

        private static void AddIfFile(List<string> list, string path)
        {
            if (File.Exists(path)) list.Add(path);
        }

        private static List<string> Dedupe(IEnumerable<string> items, bool caseInsensitive)
        {
            var seen = new HashSet<string>(caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var outList = new List<string>();
            foreach (var i in items)
            {
                if (seen.Add(i)) outList.Add(i);
            }

            return outList;
        }
    }
}
