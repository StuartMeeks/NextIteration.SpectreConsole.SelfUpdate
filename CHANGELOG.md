# Changelog

All notable changes to `NextIteration.SpectreConsole.SelfUpdate` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.1.3] — 2026-05-03

### Added

- **`SelfUpdaterOptions.PreservePaths`.** Glob list (`appsettings.Development.json`, `appsettings.*.json`, `data/**`, `*.db`, …) telling the installer which top-level entries in the install directory belong to the user, not the package. Matched entries are skipped in Phase 1 (not moved into `.old/`) and don't get clobbered by a new release in Phase 2. Defaults to empty — current consumers get unchanged behaviour until they opt in.
- **Per-conflict resolver.** When a new release ships an entry whose path matches a `PreservePaths` pattern, `ISelfUpdater.InstallAsync` and `IUpdateInstaller.InstallAsync` accept an optional `Func<UpdateConflict, CancellationToken, Task<UpdateConflictResolution>>?` resolver. `null` (default) keeps the user's file. Headless callers can return a constant; interactive callers can prompt per file. New `UpdateConflict` record carries `RelativePath`, `ExistingSizeBytes`, `NewSizeBytes`.
- **`update --strategy ask|keep|new`.** New flag on `UpdateCommand`. With `--yes`, defaults to `keep` so updates never block on a prompt. Without `--yes`, defaults to `ask` and uses Spectre's `Confirm` per conflict.

### Changed

- Layered config support is documented in the README: end-user CLIs can read additional `PreservePaths` entries from `appsettings*.json` via `IConfiguration` and merge with the in-code list — no new package API needed.

---

## [0.1.2] — 2026-05-03

### Fixed

- **Symbol package now actually contains symbols.** The previous combo (`<IncludeSymbols>true</IncludeSymbols>` + `<SymbolPackageFormat>snupkg</SymbolPackageFormat>` + `<DebugType>embedded</DebugType>`) produced an empty `.snupkg` because debug info was embedded inside the `.dll`; nuget.org rejects empty symbol packages with HTTP 400. v0.1.1's `.nupkg` was published successfully but the symbol upload failed. Switching to `<DebugType>portable</DebugType>` produces a real `.pdb` next to the `.dll`; the `.snupkg` now ships it; nuget.org accepts the symbol upload; consumers debugging into the library get sources via the nuget.org symbol server. Same fix landed across all four sibling repos (Splash 0.1.2, Auth 0.6.2, Auth.Providers 0.2.2 / 0.2.2 / 0.3.2).

---

## [0.1.1] — 2026-05-03

Coordinated patch driven by an external code review.

### Security

- **Lock before staging mutation.** `UpdateInstaller.InstallAsync` now acquires `.update.lock` before any change to `.update/<tag>/`. Previously a second installer could wipe a first installer's in-flight staging directory on its way to losing the lock race.
- **Asset-name validation.** New `UpdateInstaller.ValidateAssetName` rejects path separators, parent references, rooted paths, and any name whose `Path.GetFileName` doesn't round-trip — closing a path-traversal vector for malicious or misconfigured sources.
- **HTTPS enforcement in `HttpManifestSource`.** Plain-HTTP manifest URLs and asset URLs are now rejected by default. Opt in via `SelfUpdaterOptions.AllowInsecureManifestSource = true` for tests, internal mirrors on a trusted network, and local development. Plain HTTP defeats SHA-256 verification because the SHA itself is MITM-able.

### Fixed

- **Rollback on swap failure.** A copy failure mid-swap now restores the install directory from `.old/` instead of leaving it half-populated. New `UpdateInstaller.RestoreFromOld` helper.
- **TOCTOU-safe install path.** `UpdateCommand` now fetches the release once and installs that exact instance — no second source query between display and install. New `ISelfUpdater.GetLatestReleaseAsync()` and `ISelfUpdater.InstallAsync(RemoteRelease, ...)` overloads. The parameterless `InstallAsync` is kept as a convenience for non-interactive consumers (TOCTOU window documented).

### Changed

- `<GeneratePackageOnBuild>` is now Release-only; ordinary `dotnet build` and `dotnet test` no longer produce `.nupkg` files. Output path moved from `C:\nuget-local\` to `$(MSBuildThisFileDirectory)..\..\artifacts\packages` — platform-neutral, repo-local, and gitignored.
- Test-suite count: 86 → 150.

---

## [0.1.0] — 2026-05-03

Initial commit. Never published to nuget.org — superseded by 0.1.1 before the first tag was cut.

### Added — initial public release

- **Pluggable update sources** — `IUpdateSource` contract with three built-in implementations:
  - `HttpGitHubReleaseSource` (default) — public GitHub Releases via HttpClient.
  - `GhCliReleaseSource` — private GitHub repos via the `gh` CLI.
  - `HttpManifestSource` — generic HTTPS JSON manifest hosted on any web server / blob store.
- **Asset resolution** — format-agnostic default resolver (`.zip` or `.tar.gz`) keyed on running RID with a sensible fallback chain. Override via `IAssetResolver`.
- **Verification pipeline** — multi-instance `IPackageVerifier`. Default SHA-256 verifier reads a `SHA256SUMS.txt` manifest. Pluggable for minisign / cosign / Authenticode.
- **Atomic file swap** — staged download under `.update/`, file lock, swap into install dir with previous files moved to `.old/`, automatic `.old/` cleanup on next startup.
- **Drop-in `update` command** — `CommandConfiguratorExtensions.AddUpdateCommand()` for a single command, `AddUpdateBranch()` for `update check` / `update apply`.
- **Background check + post-run banner** — `UpdateBanner.KickOffCheck()` and `RenderIfAvailable()` mirror the pl-app UX.
- **Channels & pre-releases** — `Channel` option flows through every source.
- **Configurable cache, timeouts, opt-out env var, dev-build skip predicate.**
- DI wiring via `ServiceCollectionExtensions.AddSelfUpdater(...)`.
- Full XML documentation on the public surface, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest`.
- SourceLink, deterministic builds, published symbol packages.

[0.1.3]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.3
[0.1.2]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.2
[0.1.1]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.1
