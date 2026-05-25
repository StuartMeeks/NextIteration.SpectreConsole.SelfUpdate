# Changelog

All notable changes to `NextIteration.SpectreConsole.SelfUpdate` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.1.6] — 2026-05-25

### Fixed

- **`.old/` cleanup leaves a few stragglers on Windows when OneDrive (or antivirus / Windows Search) is syncing the install directory.** The swap moves the previous install's files into `.old/`, OneDrive picks them up for sync within seconds, and the next-startup `CleanupOldInstall` recursive delete races OneDrive's open handles — most files delete fine, the ones OneDrive is still touching throw `IOException("being used by another process")`, the catch-all swallows it, and the user sees `.old/` persist (often near-empty). Same race affects `SwapAsync`'s `.old/` reset and the staging `ResetStaging`. Read-only files extracted from archives hit a parallel `UnauthorizedAccessException` for similar reasons.
- Fix: new internal `UpdateInstaller.DeleteDirectoryRobustly(path)` helper that (1) clears the `ReadOnly` attribute on every descendant file before each attempt and (2) retries the recursive delete on `IOException` / `UnauthorizedAccessException` at 200/400/800 ms — a ~1.4 s total budget tuned for OneDrive / AV / Search handle release latency. Applied to all four recursive-delete sites in `UpdateInstaller` (`CleanupOldInstall`, `SwapAsync`'s `.old/` reset, `ResetStaging`, `TryDeleteDirectory`). `CleanupOldInstall` still swallows on final failure (non-fatal — next startup will retry); the other three still throw (callers depend on the install being able to fail loudly).
- Test seam: the helper accepts injectable `deleter` and `sleeper` callbacks so unit tests can simulate transient sharing-violations without a real Windows lock.

### Why a retry rather than detecting OneDrive

OneDrive detection is fragile (registry queries, reparse-point sniffing) and the retry strategy generalises to antivirus, Windows Search, indexers, backup agents — anything that opens a transient handle on a freshly-moved file. The cost when no contention exists is one no-op attribute walk over a tree we're about to delete: negligible.

---

## [0.1.5] — 2026-05-23

### Fixed

- **`GhCliReleaseSource` returns null when `--prerelease` / a `Channel` is in play.** The list path asked `gh release list --json tagName,name,url,publishedAt,isDraft,isPrerelease` — but `gh release list` exposes a narrower field set than `gh release view`, and `url` is view-only. gh exited non-zero ("Unknown JSON field: \"url\""), `GhProcess` threw, the source's catch-all swallowed it, and consumers saw "Could not determine the latest release." Surfaced by pl-app running `update --prerelease` against a private repo whose only releases were prereleases. Fix: drop `name` and `url` from the list `--json` value — neither was read from the list result anyway (only `tagName`, `publishedAt`, `isDraft`, `isPrerelease` drive filter/sort). The full detail (incl. `url`, `assets`) is still fetched per-tag via `gh release view`. New regression test in `GhCliReleaseSourceTests` asserts the list args stay within `release list`'s supported fields.

### Why this slipped through v0.1.4

- The existing tests use a fake gh runner with canned JSON, so they never exercised the real gh CLI's field-validation. `gh release list` was only reached when `Channel` was set or `IncludePrereleases` was `true` at the source — both uncommon configs before `--prerelease` landed.

---

## [0.1.4] — 2026-05-23

### Added

- **`update --prerelease` and `update check --prerelease`.** Opt into prerelease tags for a single command invocation without touching the DI-registered `SelfUpdaterOptions.IncludePrereleases` default. Useful for downstream apps testing RC builds. Help text: `Consider GitHub prereleases when looking for the latest version (off by default).`
- **`bool? includePrereleasesOverride` parameter** on `IUpdateSource.GetLatestAsync`, `IUpdateChecker.CheckAsync`, and `ISelfUpdater.GetLatestReleaseAsync`, added as default interface methods that delegate to the existing overload. External `IUpdateSource` implementers continue to compile unchanged; they only need to override the new overload if they want to honour `--prerelease`. `null` defers to the source's captured default; `true`/`false` force inclusion or exclusion.

### Changed

- The update-check cache now keys on `(channel, includePrereleases)` so a `--prerelease` answer doesn't pollute the next default `update check`, and vice versa. Cache files written by v0.1.3 are read as non-prerelease (matches their actual provenance — prereleases were always opt-in at DI registration). No migration needed; the new field is nullable.

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

[0.1.6]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.6
[0.1.5]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.5
[0.1.4]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.4
[0.1.3]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.3
[0.1.2]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.2
[0.1.1]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.1
