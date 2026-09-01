using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.ProjectSystem.Query;
using SocklessNpmManager.Core.Hosting;
using SocklessNpmManager.Core.Model;

namespace SocklessNpmManager.Vs.Hosting
{
    /// <summary>
    /// Turns the currently open solution / selected project into a <see cref="HostScope"/> for Core,
    /// using the VisualStudio.Extensibility Project Query API.
    /// </summary>
    internal static class ScopeResolver
    {
        /// <summary>Scope = every project directory in the solution (the VS Code multi-root behaviour).</summary>
        public static async Task<HostScope> ForSolutionAsync(VisualStudioExtensibility extensibility, CancellationToken cancellationToken)
        {
            var workspaces = extensibility.Workspaces();

            var roots = new List<string>();
            try
            {
                var projects = await workspaces.QueryProjectsAsync(p => p.With(x => x.Path), cancellationToken).ConfigureAwait(false);
                foreach (var project in projects)
                {
                    var dir = SafeDirectory(project.Path);
                    if (dir != null && !roots.Contains(dir, StringComparer.OrdinalIgnoreCase)) roots.Add(dir);
                }
            }
            catch
            {
                // fall through to the solution directory
            }

            if (roots.Count == 0)
            {
                var solutionDir = await SolutionDirectoryAsync(extensibility, cancellationToken).ConfigureAwait(false);
                if (solutionDir != null) roots.Add(solutionDir);
            }

            return new HostScope { Mode = ScopeMode.Solution, Roots = roots };
        }

        /// <summary>Scope = a single project directory.</summary>
        public static async Task<HostScope> ForProjectAsync(VisualStudioExtensibility extensibility, string? selectedPath, CancellationToken cancellationToken)
        {
            var dir = SafeDirectory(selectedPath);

            if (dir == null)
            {
                // No usable selection — fall back to the first project in the solution.
                try
                {
                    var projects = await extensibility.Workspaces()
                        .QueryProjectsAsync(p => p.With(x => x.Path), cancellationToken).ConfigureAwait(false);
                    dir = projects.Select(p => SafeDirectory(p.Path)).FirstOrDefault(d => d != null);
                }
                catch
                {
                    // ignored
                }
            }

            return dir != null
                ? new HostScope { Mode = ScopeMode.Project, Roots = new[] { dir } }
                : await ForSolutionAsync(extensibility, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string?> SolutionDirectoryAsync(VisualStudioExtensibility extensibility, CancellationToken cancellationToken)
        {
            try
            {
                var solutions = await extensibility.Workspaces()
                    .QuerySolutionAsync(s => s.With(x => x.Path), cancellationToken).ConfigureAwait(false);
                var path = solutions.FirstOrDefault()?.Path;
                return SafeDirectory(path);
            }
            catch
            {
                return null;
            }
        }

        private static string? SafeDirectory(string? fileOrDir)
        {
            if (string.IsNullOrWhiteSpace(fileOrDir)) return null;
            try
            {
                if (Directory.Exists(fileOrDir)) return Path.GetFullPath(fileOrDir);
                var dir = Path.GetDirectoryName(fileOrDir);
                return string.IsNullOrEmpty(dir) ? null : Path.GetFullPath(dir);
            }
            catch
            {
                return null;
            }
        }
    }
}
