using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.ProjectSystem.Query;
using SocklessNpmManager.Vs.Hosting;
using SocklessNpmManager.Vs.ToolWindows;

namespace SocklessNpmManager.Vs.Commands
{
    /// <summary>
    /// Opens the manager scoped to a single project. Placed on the project and project-item context
    /// menus; the scope comes from the clicked node's <c>IProjectSnapshot.Path</c>, else the
    /// selected item path.
    /// </summary>
    [VisualStudioContribution]
    internal sealed class OpenNpmManagerForProjectCommand : Command
    {
        private readonly NpmManagerSession _session = NpmManagerSession.Shared;

        public override CommandConfiguration CommandConfiguration => new("%SocklessNpmManager.OpenManagerForProject.DisplayName%")
        {
            Placements = new[]
            {
                CommandPlacements.ProjectContextMenu,
                CommandPlacements.FileInProjectContextMenu,
            },
            Icon = new(ImageMoniker.KnownValues.NuGet, IconSettings.IconAndText),
        };

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            try
            {
                string? projectPath = null;

                try
                {
                    var project = await context.GetActiveProjectAsync(p => p.With(x => x.Path), cancellationToken).ConfigureAwait(false);
                    projectPath = project?.Path;
                }
                catch
                {
                    // fall back to the raw selection
                }

                if (projectPath == null)
                {
                    try
                    {
                        var uri = await context.GetSelectedPathAsync(cancellationToken).ConfigureAwait(false);
                        if (uri != null && uri.IsFile) projectPath = uri.LocalPath;
                    }
                    catch
                    {
                        // ignored
                    }
                }

                var scope = await ScopeResolver.ForProjectAsync(this.Extensibility, projectPath, cancellationToken).ConfigureAwait(false);
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
