# Sockless npm Package Manager for Visual Studio

A visual npm package manager for **Visual Studio 2026** — browse, install, update and
consolidate `package.json` dependencies with a Visual Studio–native experience.

This is the Visual Studio edition of the
[VS Code extension](https://github.com/sockless-coding/vs-code-npm-manager). The two
repositories are independent; all npm/registry/project logic here is a fresh C#
implementation.

- Open the manager from a **project** (right-click → *Manage npm Packages…*) to limit
  it to that project.
- Open it from the **solution** node to work across every project in the solution,
  like the VS Code multi-root view.

## Solution layout

| Project | Framework | Role |
|---|---|---|
| `src/SocklessNpmManager.Core` | `netstandard2.0` | Host-agnostic domain logic. No Visual Studio references — a future Rider/CLI/VSSDK host just implements `IHostBridge`. |
| `src/SocklessNpmManager.Core.Tests` | `net10.0` | xUnit tests, ported from the VS Code extension's `test/unit`. |
| `src/SocklessNpmManager.Vs` | `net10.0-windows` | The VisualStudio.Extensibility (out-of-process) extension. Thin host over Core. |
| `tools/SocklessNpmManager.Harness` | `net10.0` | Console end-to-end check for Core without Visual Studio. |

### Core modules (ports of the VS Code extension)

| Core type | Ported from |
|---|---|
| `Npm/NpmHttpClient` | `src/npm/httpClient.ts` |
| `Npm/NpmrcParser` | `src/npm/npmrc.ts` |
| `Npm/RegistryService` | `src/npm/registries.ts` |
| `Npm/SearchService` | `src/npm/search.ts` |
| `Npm/MetadataService` | `src/npm/metadata.ts` |
| `Npm/SemverUtil`, `Npm/VersionRange` | `src/npm/semverUtil.ts`, `src/npm/versionRange.ts` |
| `Npm/PackageAge` | `src/webview/packageAge.ts` |
| `Projects/PackageJsonReader`, `Projects/PackageJsonEditor` | `src/projects/packageJson.ts`, `jsonEdit.ts` |
| `Projects/ProjectRegistry` | `src/projects/discovery.ts` |
| `Projects/LockGraph` | `src/projects/lockGraph.ts` |
| `Projects/Advisories` | `src/projects/advisories.ts` |
| `Projects/InstalledService` | `src/projects/installed.ts` |
| `Projects/MutationService` | `src/projects/mutations.ts` |
| `Cli/PackageManagerCli`, `Cli/ProcessRunner` | `src/node/cli.ts` |
| `Hosting/NpmManagerController` | the `Controller` class in `src/extension.ts` |
| `Hosting/IHostBridge` | the VS Code API surface (`workspace`, `secrets`, `window`, `env`) |

## Requirements

- Visual Studio 2026 with the *Visual Studio extension development* workload.
- Node.js + npm (and optionally Yarn / pnpm) on `PATH` for update checks, `npm audit`
  and installs. `npmManager.packageManagerPath` / `npmManager.nodePath` override the
  discovered executables.

## Building

```
dotnet build SocklessNpmManager.slnx
dotnet test  SocklessNpmManager.slnx
```

Building `src/SocklessNpmManager.Vs` produces `SocklessNpmManager.Vs.vsix`.

Exercise Core without Visual Studio:

```
dotnet run --project tools/SocklessNpmManager.Harness -- <path-to-a-workspace> express
```

## Status

**Done and tested**

- Full C# port of the domain logic (`SocklessNpmManager.Core`).
- 43 unit tests (`versionRange`, `npmrc`, `jsonEdit`, `lockGraph`, `advisories`,
  `packageAge`, `semverUtil`, `globMatcher`) — all green.
- End-to-end console harness: project discovery, registry discovery, search, package
  detail, the two-phase Installed snapshot and streamed enrichment all verified
  against a real workspace + the public npm registry.
- VisualStudio.Extensibility project compiles and produces a `.vsix`; `Extension`
  entry point, DI, `VsHostBridge` (config / DPAPI secrets / logger / file watch) and
  `NpmManagerSession` are wired to Core.

**In progress**

- `OpenNpmManagerCommand` — **Manage npm Packages…** on the Extensions menu,
  View › Other Windows, and the solution / project context menus.
- `NpmManagerToolWindow` + Remote UI (`NpmManagerToolWindowControl.xaml`) showing the
  **Installed** view: package list with requested → latest version and badges
  (transitive / pinned / deprecated / vulnerable / just-released), a Refresh button,
  and streamed enrichment.
- `ScopeResolver` — resolves the open solution's projects to scope roots via the
  Project Query API.

**Not yet built**

- Per-node scoping: the command currently always scopes to the whole solution;
  reading the clicked project vs solution node (via `IClientContext` /
  `ClientContextKey.Shell.ActiveSelectionPath`) is next.
- The Browse / Updates / Consolidate tabs, the package detail pane, and the
  install / update / uninstall / pin actions.
- VisualStudio.Extensibility Settings API wiring (`VsHostConfig` currently returns
  the VS Code defaults), a real Output window pane, and a credential-entry dialog
  (`.npmrc` token auth already works; interactive entry does not yet).

## License

MIT
