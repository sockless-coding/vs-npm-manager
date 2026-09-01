using System;
using System.Linq;
using System.Threading.Tasks;
using SocklessNpmManager.Core.Hosting;
using SocklessNpmManager.Core.Model;

namespace SocklessNpmManager.Harness
{
    /// <summary>
    /// Manual end-to-end check for Core without Visual Studio.
    ///
    ///   dotnet run --project tools/SocklessNpmManager.Harness -- &lt;workspace-dir&gt; [search-term]
    /// </summary>
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            var root = args.Length > 0 ? args[0] : Environment.CurrentDirectory;
            var term = args.Length > 1 ? args[1] : "express";

            var bridge = new ConsoleHostBridge(root, ScopeMode.Solution);
            using var controller = new NpmManagerController(bridge);

            controller.Progress += (msg, done) => Console.WriteLine(done ? "[progress] done" : $"[progress] {msg}");
            controller.InstalledEnriched += (phase, pkgs) => Console.WriteLine($"[enriched:{phase}] {pkgs.Count} packages");

            Console.WriteLine($"== Initialising against {root} ==");
            await controller.InitializeAsync();

            var projects = controller.ListProjects();
            Console.WriteLine($"\nProjects ({projects.Count}):");
            foreach (var p in projects)
            {
                Console.WriteLine($"  {p.Name}  [{p.PackageManager}]  ws-root={p.IsWorkspaceRoot}  {p.Path}");
            }

            var registries = controller.ListRegistries();
            Console.WriteLine($"\nRegistries ({registries.Count}):");
            foreach (var r in registries)
            {
                Console.WriteLine($"  {r.Name}  {r.Url}  auth={r.RequiresAuth}");
            }

            Console.WriteLine($"\n== Search: '{term}' ==");
            try
            {
                var page = await controller.SearchAsync(term, 0, 10, includePrerelease: false, source: "All registries");
                foreach (var s in page.Results.Take(10))
                {
                    Console.WriteLine($"  {s.Id}@{s.Version}  ↓{s.TotalDownloads}  {Trim(s.Description)}");
                }

                var first = page.Results.FirstOrDefault();
                if (first != null)
                {
                    Console.WriteLine($"\n== Detail: {first.Id} ==");
                    var detail = await controller.GetPackageDetailAsync(first.Id, "All registries", false);
                    Console.WriteLine($"  selected={detail.SelectedVersion}  versions={detail.Versions.Count}  license={detail.LicenseExpression}");
                    Console.WriteLine($"  deps groups={detail.DependencyGroups.Count}  readme?={(detail.ReadmeMarkdown != null)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  search/detail failed: {ex.Message}");
            }

            Console.WriteLine("\n== Installed ==");
            var (installed, pmAvailable) = await controller.ListInstalledAsync(includeTransitive: false);
            Console.WriteLine($"  package manager available: {pmAvailable}");
            foreach (var p in installed.Take(25))
            {
                Console.WriteLine($"  {p.Id}  req={p.RequestedVersion}  resolved={p.ResolvedVersion}  latest={p.LatestVersion}  transitive={p.Transitive}");
            }

            // Give background enrichment a moment to stream in.
            await Task.Delay(5000);

            Console.WriteLine("\n== Done ==");
            return 0;
        }

        private static string Trim(string s) => s.Length > 80 ? s.Substring(0, 80) + "…" : s;
    }
}
