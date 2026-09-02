using Microsoft.VisualStudio.Extensibility;

namespace SocklessNpmManager.Vs
{
    /// <summary>The VisualStudio.Extensibility entry point for the npm Package Manager.</summary>
    [VisualStudioContribution]
    public sealed class NpmManagerExtension : Extension
    {
        public override ExtensionConfiguration ExtensionConfiguration => new()
        {
            Metadata = new(
                id: "SocklessNpmManager.Vs.6b2f7d1e-0d4b-4b8f-9d1a-3f9c2a5e7c10",
                version: this.ExtensionAssemblyVersion,
                publisherName: "sockless-coding",
                displayName: "Sockless npm Package Manager",
                description: "Visual npm package manager for Visual Studio — browse, install, update and consolidate packages.")
            {
                // Driven by $(NpmVsDotnet) in the csproj — see the csproj header.
                DotnetTargetVersions = new[]
                {
#if NPMVS_NET_FUTURE
                    DotnetTarget.Custom("net10.0"),
#else
                    DotnetTarget.Net8,
#endif
                },
            },
        };
    }
}
