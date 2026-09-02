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

### .NET runtime

The extension targets **`net8.0-windows8.0`**, driven by the `$(NpmVsDotnet)`
property in `SocklessNpmManager.Vs.csproj`.

VisualStudio.Extensibility runs out-of-process extensions on **.NET 8 only** as of
now — the VS Extensibility team is explicit about this on
[issue #544](https://github.com/microsoft/VSExtensibility/issues/544) ("you
shouldn't target anything above net8.0 … stick with net8.0-windows8.0"), and
`net10.0` builds fail to run in VS 2026. .NET 10 host support is stated to be
"coming in the near future".

This isn't the same as shipping an end-of-life runtime: Visual Studio bundles the
runtime the extension host uses, and Microsoft's
[Feb 2025 policy](https://devblogs.microsoft.com/visualstudio/visualstudio-extensibility-managing-net-runtime-versions/)
commits to keeping it in service. When VS moves the host to .NET 10, the migration
here is one flag:

```
dotnet build -p:NpmVsDotnet=net10.0
```

which flips both the target framework and the `DotnetTargetVersions` manifest
metadata. Everything else — all of `SocklessNpmManager.Core` — is `netstandard2.0`
and unaffected.
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

**In progress — the VS extension**

- **Commands** — `OpenNpmManagerForSolutionCommand` (Extensions menu, View ›
  Other Windows, solution context menu) scopes to every project in the solution;
  `OpenNpmManagerForProjectCommand` (project context menu) scopes to the clicked
  project. `ScopeResolver` resolves both via the Project Query API.
- **Tool window** — `NpmManagerToolWindow` + Remote UI: the Browse / Installed /
  Updates / Consolidate tabs, a toolbar (search, prerelease toggle, registry
  picker), the package list, and a detail pane with the version / save-as /
  add-as pickers, a per-`package.json` checklist, the Install / Update /
  Uninstall / Pin / Unpin actions, advisories, dependency groups and the readme.
  Update All, streamed enrichment, the no-package-manager banner and a toast are
  wired.

**Remote UI notes** (learned from running in VS 2026)

- `BooleanToVisibilityConverter` and binding `StringFormat` do not work in Remote UI.
  Conditional visibility is exposed as `System.Windows.Visibility` properties on the
  view-models; computed label strings replace `StringFormat`.
- VS theming comes from *implicit* styles (`<Style TargetType="Button" BasedOn=…>`)
  in the root `Grid.Resources`, per Microsoft's documented pattern.
- The data context loads in `RemoteUserControl.ControlLoadedAsync` so it captures the
  right `SynchronizationContext` for streamed updates.

**Not yet built**

- VisualStudio.Extensibility Settings API wiring (`VsHostConfig` currently returns
  the VS Code defaults), a real Output window pane, and a credential-entry dialog
  (`.npmrc` token auth already works; interactive entry does not yet).
- The Installed tree (transitive nesting with "why"); the list is currently flat.

## License

MIT
