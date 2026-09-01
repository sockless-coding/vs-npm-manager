using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using SocklessNpmManager.Vs.Hosting;
using SocklessNpmManager.Vs.ToolWindows;

namespace SocklessNpmManager.Vs.Commands
{
    /// <summary>
    /// Opens the npm Package Manager tool window. Placed on the Extensions menu, View ›
    /// Other Windows, and the Solution Explorer solution / project context menus.
    /// </summary>
    [VisualStudioContribution]
    internal sealed class OpenNpmManagerCommand : Command
    {
        // guidSHLMainMenu — the shell command set that owns the built-in context menus.
        private static readonly Guid ShlMainMenu = new("d309f791-903f-11d0-9efc-00a0c911004f");
        private const uint IdmVsCtxtSolutionNode = 0x0439;
        private const uint IdmVsCtxtProjectNode = 0x0402;

        private readonly NpmManagerSession _session;

        public OpenNpmManagerCommand(NpmManagerSession session)
        {
            _session = session;
        }

        public override CommandConfiguration CommandConfiguration => new("%SocklessNpmManager.OpenManager.DisplayName%")
        {
            Placements = new[]
            {
                CommandPlacement.KnownPlacements.ExtensionsMenu,
                CommandPlacement.KnownPlacements.ViewOtherWindowsMenu,
                CommandPlacement.VsctParent(ShlMainMenu, IdmVsCtxtSolutionNode, 0x0100),
                CommandPlacement.VsctParent(ShlMainMenu, IdmVsCtxtProjectNode, 0x0100),
            },
            Icon = new(ImageMoniker.KnownValues.NuGet, IconSettings.IconAndText),
        };

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            // Iteration 1: always scope to the whole solution. Per-node scoping (project vs
            // solution, from the clicked item) is the next step and uses IClientContext /
            // ClientContextKey.Shell.ActiveSelectionPath.
            var scope = await ScopeResolver.ForSolutionAsync(this.Extensibility, cancellationToken).ConfigureAwait(false);
            await _session.SetScopeAsync(scope).ConfigureAwait(false);
            await this.Extensibility.Shell().ShowToolWindowAsync<NpmManagerToolWindow>(activate: true, cancellationToken).ConfigureAwait(false);
        }
    }
}
