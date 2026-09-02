using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using SocklessNpmManager.Vs.Hosting;
using SocklessNpmManager.Vs.ToolWindows;

namespace SocklessNpmManager.Vs.Commands
{
    /// <summary>
    /// Opens the manager scoped to every project in the solution (the VS Code multi-root view).
    /// Placed on the Extensions menu, View › Other Windows, and the solution context menu.
    /// </summary>
    [VisualStudioContribution]
    internal sealed class OpenNpmManagerForSolutionCommand : Command
    {
        private readonly NpmManagerSession _session = NpmManagerSession.Shared;

        public override CommandConfiguration CommandConfiguration => new("%SocklessNpmManager.OpenManagerForSolution.DisplayName%")
        {
            Placements = new[]
            {
                CommandPlacement.KnownPlacements.ExtensionsMenu,
                CommandPlacement.KnownPlacements.ViewOtherWindowsMenu,
                CommandPlacements.SolutionContextMenu,
            },
            Icon = new(ImageMoniker.KnownValues.NuGet, IconSettings.IconAndText),
        };

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            try
            {
                var scope = await ScopeResolver.ForSolutionAsync(this.Extensibility, cancellationToken).ConfigureAwait(false);
                await _session.SetScopeAsync(scope).ConfigureAwait(false);
                await this.Extensibility.Shell().ShowToolWindowAsync<NpmManagerToolWindow>(activate: true, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await this.Extensibility.Shell().ShowPromptAsync(
                    "npm Package Manager failed to open:\n\n" + ex, PromptOptions.ErrorConfirm, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
