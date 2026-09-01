using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SocklessNpmManager.Core.Cli;
using SocklessNpmManager.Core.Hosting;
using SocklessNpmManager.Core.Model;
using SocklessNpmManager.Core.Npm;

namespace SocklessNpmManager.Core.Projects
{
    /// <summary>
    /// Applying install / update / uninstall / pin / unpin. Port of <c>src/projects/mutations.ts</c>.
    ///
    /// Only "caret" (a CLI's own default) and "exact" (<c>--save-exact</c>) map onto an actual CLI
    /// flag; a tilde or ">=" range, pin/unpin, and any case where the package manager isn't available
    /// all fall back to a format-preserving <c>package.json</c> edit via <see cref="PackageJsonEditor"/>,
    /// which reports <c>InstallNeeded</c> so the lockfile can be refreshed afterwards.
    /// </summary>
    public sealed class MutationService
    {
        private readonly ProjectRegistry _projects;
        private readonly PackageManagerCli _cli;
        private readonly IHostBridge _host;

        public MutationService(ProjectRegistry projects, PackageManagerCli cli, IHostBridge host)
        {
            _projects = projects;
            _cli = cli;
            _host = host;
        }

        public async Task<MutationResult> ApplyAsync(MutationRequest req, string? registryUrl = null)
        {
            var result = new MutationResult
            {
                Ok = true,
                Action = req.Action,
                PackageId = req.PackageId,
            };

            var touchedDirs = new Dictionary<string, WorkspaceProject>(StringComparer.OrdinalIgnoreCase);

            foreach (var projectPath in req.ProjectPaths)
            {
                var project = _projects.FindByPath(projectPath);
                if (project == null)
                {
                    result.PerProject.Add(new ProjectMutationResult
                    {
                        Project = InstalledService.ProjectDisplayName(projectPath),
                        Ok = false,
                        Message = "package.json not found",
                    });
                    result.Ok = false;
                    continue;
                }

                var pmAvailable = await _cli.IsAvailableAsync(project.PackageManager).ConfigureAwait(false);
                try
                {
                    if (req.Action == InstallAction.Pin || req.Action == InstallAction.Unpin)
                    {
                        ApplyPin(req, project, req.Action);
                        result.UsedFallback = true;
                    }
                    else if (req.Action == InstallAction.Uninstall)
                    {
                        if (pmAvailable)
                        {
                            await ApplyRemoveWithCliAsync(req, project).ConfigureAwait(false);
                        }
                        else
                        {
                            ApplyRemoveWithJson(req, project);
                            result.UsedFallback = true;
                        }
                    }
                    else
                    {
                        var dependencyType = req.Action == InstallAction.Update
                            ? CurrentType(project, req.PackageId)
                            : req.DependencyType ?? DependencyType.Dependencies;
                        var prefix = ResolvePrefix(req, project);

                        var canUseCli = pmAvailable
                                        && (prefix == VersionPrefix.Exact || prefix == VersionPrefix.Caret)
                                        && _cli.SupportsAddType(project.PackageManager, dependencyType);
                        if (canUseCli)
                        {
                            await ApplyAddWithCliAsync(req, project, dependencyType, prefix == VersionPrefix.Exact, registryUrl).ConfigureAwait(false);
                        }
                        else
                        {
                            ApplyAddWithJson(req, project, dependencyType, prefix);
                            result.UsedFallback = true;
                        }
                    }

                    touchedDirs[project.Dir] = project;
                    result.PerProject.Add(new ProjectMutationResult { Project = project.Info.Name, Ok = true });
                }
                catch (Exception ex)
                {
                    result.Ok = false;
                    result.PerProject.Add(new ProjectMutationResult { Project = project.Info.Name, Ok = false, Message = ex.Message });
                }
            }

            if (result.Ok && _host.Config.GetBool(SettingKeys.AutoInstall, true))
            {
                foreach (var project in touchedDirs.Values)
                {
                    if (!await _cli.IsAvailableAsync(project.PackageManager).ConfigureAwait(false))
                    {
                        result.InstallNeeded = true;
                        continue;
                    }

                    var r = await _cli.InstallAsync(project.PackageManager, project.Dir).ConfigureAwait(false);
                    if (r.Code != 0) result.InstallNeeded = true;
                }
            }
            else if (result.Ok)
            {
                result.InstallNeeded = true;
            }

            await _projects.RefreshAsync().ConfigureAwait(false);
            return result;
        }

        private async Task ApplyRemoveWithCliAsync(MutationRequest req, WorkspaceProject project)
        {
            var r = await _cli.RemovePackageAsync(project.PackageManager, project.Dir, req.PackageId).ConfigureAwait(false);
            if (r.Code != 0) throw new Exception(LastLine(r.Stderr.Length > 0 ? r.Stderr : r.Stdout) is { Length: > 0 } m ? m : $"{project.PackageManager.ToCliName()} remove failed");
        }

        private async Task ApplyAddWithCliAsync(MutationRequest req, WorkspaceProject project, DependencyType dependencyType, bool exact, string? registryUrl)
        {
            var r = await _cli.AddPackageAsync(project.PackageManager, project.Dir, req.PackageId, req.Version, dependencyType, exact, registryUrl).ConfigureAwait(false);
            if (r.Code != 0) throw new Exception(LastLine(r.Stderr.Length > 0 ? r.Stderr : r.Stdout) is { Length: > 0 } m ? m : $"{project.PackageManager.ToCliName()} add failed");
        }

        private void ApplyRemoveWithJson(MutationRequest req, WorkspaceProject project)
        {
            _host.Logger.Line($"[json] uninstall {req.PackageId} in {project.Info.Name}");
            EditFile(project.Info.Path, t => PackageJsonEditor.RemoveDependency(t, req.PackageId));
        }

        private void ApplyAddWithJson(MutationRequest req, WorkspaceProject project, DependencyType dependencyType, VersionPrefix prefix)
        {
            if (string.IsNullOrEmpty(req.Version)) throw new Exception("A target version is required");
            var range = VersionRange.ApplyVersionPrefix(req.Version!, prefix);
            _host.Logger.Line($"[json] {req.Action.ToString().ToLowerInvariant()} {req.PackageId}@{range} in {project.Info.Name}");
            EditFile(project.Info.Path, t => PackageJsonEditor.UpsertDependency(t, req.PackageId, range, dependencyType));
        }

        private DependencyType CurrentType(WorkspaceProject project, string packageId)
        {
            var key = packageId.ToLowerInvariant();
            return project.Parsed.Dependencies.FirstOrDefault(r => r.Id.ToLowerInvariant() == key)?.DependencyType ?? DependencyType.Dependencies;
        }

        /// <summary>
        /// The version selector to write: whatever the caller explicitly chose, else the selector the
        /// project's current reference already uses (so an unrelated Update doesn't silently change a
        /// package's range style), else "caret" for a fresh install.
        /// </summary>
        private VersionPrefix ResolvePrefix(MutationRequest req, WorkspaceProject project)
        {
            if (req.VersionPrefix.HasValue) return req.VersionPrefix.Value;
            if (req.Action == InstallAction.Update)
            {
                var key = req.PackageId.ToLowerInvariant();
                var reference = project.Parsed.Dependencies.FirstOrDefault(r => r.Id.ToLowerInvariant() == key);
                if (reference != null) return VersionRange.DetectVersionPrefix(reference.Version);
            }

            return VersionPrefix.Caret;
        }

        /// <summary>Write the version as a bare exact version (pin) or a caret range (unpin), preserving file formatting.</summary>
        private void ApplyPin(MutationRequest req, WorkspaceProject project, InstallAction mode)
        {
            if (string.IsNullOrEmpty(req.Version)) throw new Exception("A target version is required");
            var version = mode == InstallAction.Pin ? VersionRange.ToExactVersionPin(req.Version!) : VersionRange.ToCaretRange(req.Version!);
            var dependencyType = CurrentType(project, req.PackageId);
            _host.Logger.Line($"[json] {mode.ToString().ToLowerInvariant()} {req.PackageId}@{version} in {project.Info.Name}");
            EditFile(project.Info.Path, t => PackageJsonEditor.UpsertDependency(t, req.PackageId, version, dependencyType));
        }

        private static void EditFile(string filePath, Func<string, string> transform)
        {
            var original = File.ReadAllText(filePath);
            var updated = transform(original);
            if (updated != original)
            {
                File.WriteAllText(filePath, updated);
            }
        }

        private static string LastLine(string s)
        {
            var lines = Regex.Split(s, "\r\n|\n|\r").Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            return lines.Count > 0 ? lines[lines.Count - 1] : "";
        }
    }
}
