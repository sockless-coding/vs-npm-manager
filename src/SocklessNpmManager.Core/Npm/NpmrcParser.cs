using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SocklessNpmManager.Core.Npm
{
    public sealed class NpmrcUserPass
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
    }

    /// <summary>Parsed <c>.npmrc</c> contents. Port of the <c>ParsedNpmrc</c> shape in <c>src/npm/npmrc.ts</c>.</summary>
    public sealed class ParsedNpmrc
    {
        /// <summary>Default registry, e.g. from <c>registry=…</c>.</summary>
        public string? Registry { get; set; }

        /// <summary>scope (without leading <c>@</c>) → registry URL, from <c>@scope:registry=…</c>.</summary>
        public Dictionary<string, string> ScopedRegistries { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary><c>//host/path:_authToken=…</c> entries, keyed by the raw <c>host/path</c> prefix.</summary>
        public Dictionary<string, string> AuthTokens { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary><c>//host/path:_auth=…</c> (pre-encoded base64 <c>user:pass</c>).</summary>
        public Dictionary<string, string> BasicAuth { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary><c>//host/path:_username=</c> / <c>:_password=</c> (base64 password) pairs.</summary>
        public Dictionary<string, NpmrcUserPass> UserPass { get; } = new Dictionary<string, NpmrcUserPass>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Pure <c>.npmrc</c> parsing and merging. No filesystem dependency — discovery lives in
    /// <see cref="RegistryService"/>. Port of <c>src/npm/npmrc.ts</c>.
    /// Reference: https://docs.npmjs.com/cli/v10/configuring-npm/npmrc
    /// </summary>
    public static class NpmrcParser
    {
        private static readonly Regex ScopedRegistryRe = new Regex(@"^@([^:]+):registry$", RegexOptions.Compiled);
        private static readonly Regex HostAuthRe = new Regex(@"^//(.+):(_authToken|_auth|_username|_password|always-auth)$", RegexOptions.Compiled);
        private static readonly Regex EnvRefRe = new Regex(@"\$\{([^}]+)\}", RegexOptions.Compiled);

        /// <summary>Expand <c>${VAR}</c> references against the process environment, as npm does.</summary>
        private static string ExpandEnv(string value)
        {
            return EnvRefRe.Replace(value, m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? "");
        }

        public static ParsedNpmrc Parse(string text)
        {
            var result = new ParsedNpmrc();
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                var eq = line.IndexOf('=');
                if (eq < 0) continue;

                var key = line.Substring(0, eq).Trim();
                var value = ExpandEnv(line.Substring(eq + 1).Trim());

                // Strip a single layer of matching quotes, as npm's ini parser does.
                if (value.Length >= 2 &&
                    ((value[0] == '"' && value[value.Length - 1] == '"') ||
                     (value[0] == '\'' && value[value.Length - 1] == '\'')))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                if (value.Length == 0) continue;

                if (key == "registry")
                {
                    result.Registry = value;
                    continue;
                }

                var scoped = ScopedRegistryRe.Match(key);
                if (scoped.Success)
                {
                    result.ScopedRegistries[scoped.Groups[1].Value] = value;
                    continue;
                }

                var hostAuth = HostAuthRe.Match(key);
                if (hostAuth.Success)
                {
                    var hostPath = hostAuth.Groups[1].Value;
                    var kind = hostAuth.Groups[2].Value;
                    switch (kind)
                    {
                        case "_authToken":
                            result.AuthTokens[hostPath] = value;
                            break;
                        case "_auth":
                            result.BasicAuth[hostPath] = value;
                            break;
                        case "_username":
                            GetOrAdd(result.UserPass, hostPath).Username = value;
                            break;
                        case "_password":
                            GetOrAdd(result.UserPass, hostPath).Password = DecodeBase64(value);
                            break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Merge parsed configs. <paramref name="configs"/> must be ordered from lowest to highest
        /// priority (global → user → workspace root → … → nearest). Nearest wins on key conflicts.
        /// </summary>
        public static ParsedNpmrc Merge(IEnumerable<ParsedNpmrc> configs)
        {
            var merged = new ParsedNpmrc();
            foreach (var cfg in configs)
            {
                if (cfg.Registry != null) merged.Registry = cfg.Registry;
                foreach (var kv in cfg.ScopedRegistries) merged.ScopedRegistries[kv.Key] = kv.Value;
                foreach (var kv in cfg.AuthTokens) merged.AuthTokens[kv.Key] = kv.Value;
                foreach (var kv in cfg.BasicAuth) merged.BasicAuth[kv.Key] = kv.Value;
                foreach (var kv in cfg.UserPass)
                {
                    var into = GetOrAdd(merged.UserPass, kv.Key);
                    if (kv.Value.Username != null) into.Username = kv.Value.Username;
                    if (kv.Value.Password != null) into.Password = kv.Value.Password;
                }
            }

            return merged;
        }

        /// <summary>Find the auth entry whose <c>host/path</c> prefix best matches <paramref name="url"/>.</summary>
        public static string? FindAuthPrefix(IEnumerable<string> keys, string url)
        {
            string host;
            string pathname;
            try
            {
                var u = new Uri(url);
                host = u.Authority;
                pathname = u.AbsolutePath.TrimEnd('/');
            }
            catch
            {
                return null;
            }

            var full = host + pathname;
            string? best = null;
            foreach (var key in keys)
            {
                var normalized = key.TrimEnd('/');
                if (full == normalized || full.StartsWith(normalized + "/", StringComparison.Ordinal) || host == normalized)
                {
                    if (best == null || normalized.Length > best.TrimEnd('/').Length)
                    {
                        best = key;
                    }
                }
            }

            return best;
        }

        private static NpmrcUserPass GetOrAdd(Dictionary<string, NpmrcUserPass> map, string key)
        {
            if (!map.TryGetValue(key, out var entry))
            {
                entry = new NpmrcUserPass();
                map[key] = entry;
            }

            return entry;
        }

        private static string DecodeBase64(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch
            {
                return value;
            }
        }
    }
}
