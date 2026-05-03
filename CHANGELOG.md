# Changelog

All notable changes to `NextIteration.SpectreConsole.SelfUpdate` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Symbol packaging (planned 0.1.2)
- **`<DebugType>portable</DebugType>`** instead of `embedded`. The previous combination produced an empty `.snupkg` (no `.pdb` files because debug info was inside the `.dll`), which nuget.org rejects with HTTP 400. The published `.nupkg` is now slightly smaller, and the `.snupkg` actually contains symbols so consumers debugging into the library get sources via nuget.org's symbol server.

### Security & correctness fixes (planned 0.1.1)
- **Lock before staging mutation.** `UpdateInstaller.InstallAsync` now acquires `.update.lock` before any change to `.update/<tag>/`. Previously a second installer could wipe a first installer's in-flight staging directory on its way to losing the lock race.
- **Rollback on swap failure.** A copy failure mid-swap now restores the install directory from `.old/` instead of leaving it half-populated. New `UpdateInstaller.RestoreFromOld` helper.
- **Asset-name validation.** New `UpdateInstaller.ValidateAssetName` rejects path separators, parent references, rooted paths, and any name whose `Path.GetFileName` doesn't round-trip — closing a path-traversal vector for malicious or misconfigured sources.
- **HTTPS enforcement in `HttpManifestSource`.** Plain-HTTP manifest URLs and asset URLs are now rejected by default. Opt in via `SelfUpdaterOptions.AllowInsecureManifestSource = true` (tests, internal mirrors, local dev). Plain HTTP defeats SHA-256 verification because the SHA itself is MITM-able.
- **TOCTOU-safe install path.** `UpdateCommand` now fetches the release once and installs that exact instance — no second source query between display and install. New `ISelfUpdater.GetLatestReleaseAsync()` and `ISelfUpdater.InstallAsync(RemoteRelease, ...)` overloads. The parameterless `InstallAsync` is kept as a convenience for non-interactive consumers (TOCTOU window documented).

### Build hygiene
- `<GeneratePackageOnBuild>` is now Release-only; ordinary `dotnet build` and `dotnet test` no longer produce `.nupkg` files. Output path moved from `C:\nuget-local\` to `$(MSBuildThisFileDirectory)..\..\artifacts\packages` — platform-neutral, repo-local, and gitignored.

### Added — initial public release (planned 0.1.0)
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
- SourceLink, deterministic builds, embedded symbols, published symbol packages.
