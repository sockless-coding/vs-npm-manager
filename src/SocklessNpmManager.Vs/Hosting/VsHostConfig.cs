using System;
using System.Collections.Generic;
using SocklessNpmManager.Core.Hosting;

namespace SocklessNpmManager.Vs.Hosting
{
    /// <summary>
    /// Reads <c>npmManager.*</c> settings. For now it returns the same defaults the VS Code
    /// extension ships; wiring the VisualStudio.Extensibility Settings API is a follow-up
    /// (see plan phase 7). <see cref="ConfigChanged"/> will fire once that is in place.
    /// </summary>
    internal sealed class VsHostConfig : IHostConfig
    {
        public bool GetBool(string key, bool fallback) => key switch
        {
            SettingKeys.AutoInstall => true,
            SettingKeys.DefaultIncludePrerelease => false,
            SettingKeys.UsePackageManagerForEnumeration => false,
            _ => fallback,
        };

        public int GetInt(string key, int fallback) => key switch
        {
            SettingKeys.MinimumPackageAgeDays => 7,
            _ => fallback,
        };

        public string GetString(string key, string fallback) => fallback;

        public IReadOnlyList<AdditionalRegistry> GetAdditionalRegistries() => Array.Empty<AdditionalRegistry>();

        public event EventHandler? ConfigChanged
        {
            add { }
            remove { }
        }
    }
}
