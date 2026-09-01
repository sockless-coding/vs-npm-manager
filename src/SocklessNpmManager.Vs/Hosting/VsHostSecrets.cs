using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SocklessNpmManager.Core.Hosting;

namespace SocklessNpmManager.Vs.Hosting
{
    /// <summary>
    /// Registry auth tokens, persisted to a DPAPI-encrypted file under <c>%LOCALAPPDATA%</c>.
    /// VisualStudio.Extensibility has no first-class secret store yet, so this stands in for the
    /// VS Code <c>context.secrets</c> API used by <c>src/npm/registries.ts</c>.
    /// </summary>
    internal sealed class VsHostSecrets : IHostSecrets
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SocklessNpmManager.registry-tokens.v1");

        private readonly string _path;
        private readonly object _gate = new();

        public VsHostSecrets()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SocklessNpmManager");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "registry-tokens.dat");
        }

        public Task<string?> GetAsync(string key)
        {
            lock (_gate)
            {
                var store = Load();
                return Task.FromResult(store.TryGetValue(key, out var v) ? v : null);
            }
        }

        public Task StoreAsync(string key, string value)
        {
            lock (_gate)
            {
                var store = Load();
                store[key] = value;
                Save(store);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key)
        {
            lock (_gate)
            {
                var store = Load();
                if (store.Remove(key)) Save(store);
            }

            return Task.CompletedTask;
        }

        private Dictionary<string, string> Load()
        {
            try
            {
                if (!File.Exists(_path)) return new Dictionary<string, string>(StringComparer.Ordinal);
                var protectedBytes = File.ReadAllBytes(_path);
                var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(plain);
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private void Save(Dictionary<string, string> store)
        {
            try
            {
                var json = JsonConvert.SerializeObject(store);
                var plain = Encoding.UTF8.GetBytes(json);
                var protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(_path, protectedBytes);
            }
            catch
            {
                // best-effort; a failure just means the token isn't remembered
            }
        }
    }
}
