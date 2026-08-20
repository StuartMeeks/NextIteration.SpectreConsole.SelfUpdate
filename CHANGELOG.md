# Changelog

All notable changes to `NextIteration.SpectreConsole.SelfUpdate` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Changed

- **`release.yml` folded into `ci.yml`.** Publishing now happens in the same workflow run as the build, so the `publish` job pushes the artifact this run's `build` job produced — the exact bytes the gate tested. The old tag-triggered `release.yml` rebuilt from the tag and published an artifact no gate had ever seen. It also globbed `*.nupkg` when uploading, so the `.snupkg` was built and then silently never published; the glob is now `*nupkg` and symbols ship. Repointing the nuget.org Trusted Publishing policy from `release.yml` to `ci.yml` was part of the same change, because the policy is bound to a workflow filename.
- **CI now has a single aggregating gate job, `ci`, and it is the only required status check.** `build` and `test` were required directly before, which couples the branch ruleset to the matrix: `test`'s check names carry the matrix values, so adding or dropping a platform broke protection. The gate declares `needs: [build, test]` with `if: always()` and fails on any upstream result that is not success — including `skipped`, which branch protection would otherwise read as satisfied.
- **Every workflow declares `concurrency`, explicit `permissions`, and per-job `timeout-minutes`.** Superseded pushes cancel instead of stacking up, except on tags — a half-cancelled release can leave an incomplete package set on nuget.org. NuGet restore is cached on `~/.nuget/packages`.
- **`.github/dependabot.yml` rewritten.** Minor and patch bumps are grouped into one PR per ecosystem; majors are deliberately left ungrouped so each arrives separately and stays open for review. The two runtime-aligned packages carrying per-TFM floors (`Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Http`) are now under `ignore` for major updates, because an 8.x → 10.x bump on the net8 floor is never mergeable and was weekly noise.
- **`global.json` now pins the SDK**, not just the Microsoft.Testing.Platform runner: `10.0.100` with `rollForward: latestFeature`. An unpinned SDK means a contributor on an older one gets different analyzer results from CI, and with `TreatWarningsAsErrors` that is a build which fails for them and passes for everyone else.
- **`.gitignore` and `.editorconfig` replaced with the canonical copies.** The `.editorconfig` change is one line that matters: the private-field naming rule had `applicable_kinds = field`, and a `const` *is* a field, so the rule demanded `_nonceSize` for `private const int NonceSize`. An empty `required_modifiers` scopes it to instance fields. Nothing enforces these rules at build time yet (`EnforceCodeStyleInBuild` is off), so this is a no-op for the build today and correct for when it is not.

### Added

- **`SECURITY.md`**, with a scope section specific to this library: SHA-256 verification establishes integrity but not authenticity (the expected hash ships from the same release as the asset, and there is no signature checking); `AllowInsecureManifestSource` and `UseDefaultSha256Verifier = false` are documented opt-outs that defeat verification by design; archive path-traversal defence is the framework's `ZipFile`/`TarFile` guard rather than this library's; and `GhCliReleaseSource` trusts whatever `gh` is on `PATH`. Stating the boundary is the point — a report that only restates a documented limitation is not a vulnerability.
- **`CONTRIBUTING.md`, `.github/PULL_REQUEST_TEMPLATE.md` and `CLAUDE.md`.** `CLAUDE.md` records the constraints an agent would otherwise violate here — why the three-platform matrix is load-bearing, why one install-lock test returns early on Windows by design, and that `PackageValidationBaselineVersion` tracks the last shipped release.
- **CodeQL code scanning** (`codeql.yml`), weekly plus on every push and PR, with the `security-and-quality` query pack. The build is explicit rather than `autobuild`, which has been observed to pick a single TFM and silently analyse half a multi-targeted codebase.
- **Dependabot auto-merge for minor and patch bumps** (`dependabot-auto-merge.yml`), queued behind the `ci` gate. Majors are never auto-merged. Approval uses an `AUTO_MERGE_PAT` Dependabot secret owned by a code owner — an Actions secret of the same name resolves to an empty string in a Dependabot-triggered workflow, and a `GITHUB_TOKEN` approval cannot satisfy a code-owner review.

None of the above changes the library, its public surface, or the package contents.

---

## [0.3.1] — 2026-08-19

### Changed

- **The `net8.0` target is now actually tested, not just compiled.** Since 0.2.0 the package shipped `lib/net8.0/` while the test project single-targeted `net10.0` and CI installed only the 10.0.x SDK — every net8 asset was published unverified. The test project now multi-targets `net8.0;net10.0` and CI/release install the 8.0.x runtime alongside the 10.0.x SDK, so the full suite runs against both targets on Linux, macOS, and Windows (196 tests × 2 TFMs).
- **Test stack migrated from xUnit v2 to xUnit v3** (`xunit` 2.9.3 → `xunit.v3` 4.0.0). xUnit v3 test projects are self-executing Microsoft.Testing.Platform hosts, and the .NET 10 SDK dropped support for testing through the VSTest bridge entirely, so the VSTest-era packages are gone: `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, and `coverlet.collector` (a VSTest data collector that no CI job ever invoked) are no longer referenced. `dotnet test` is pointed at MTP by a `test.runner` setting in a new root `global.json`; it pins no SDK version. Test call sites now pass `TestContext.Current.CancellationToken` into cancellable async APIs, and single-element assertions use the value returned by `Assert.Single` — both required by the v3 analyzers under `TreatWarningsAsErrors=true`.
- **Package validation now runs against a published baseline.** `EnablePackageValidation` was already on but had no `PackageValidationBaselineVersion`, so it only checked framework compatibility. The baseline is now `0.3.0`: an accidental break in the public surface relative to the last release fails the build instead of shipping.
- Bumped `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Http` 10.0.10 → 10.0.11 **on the `net10.0` target only**. The `net8.0` floors stay at 8.0.2 / 8.0.1 — those are the final 8.0.x servicing versions, so per the per-TFM flooring introduced in 0.3.0 there is nothing to move. Build-only `Microsoft.SourceLink.GitHub` 10.0.301 → 10.0.400.
- Added `.github/dependabot.yml` — weekly `nuget` and `github-actions` update PRs, replacing the hand-rolled dependency-bump PRs.
- README: the `.NET` badge advertised 10.0 only, stale since 0.2.0 multi-targeted; it now reads `8.0 | 10.0`, and the install section states the supported targets.

The library's own API and behaviour are unchanged from 0.3.0 — now enforced by the package-validation baseline. The only change visible to consumers is the `net10.0` dependency floor moving to 10.0.11.

---

## [0.3.0] — 2026-07-24

### Changed

- **Runtime-aligned Microsoft platform dependencies are now floored per target framework.** In a library a `PackageReference` version is a minimum floor NuGet forces on every downstream consumer (lowest-applicable-version resolution). Since 0.2.0 multi-targeted `net8.0` alongside `net10.0` but floored `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Http` at a single `10.0.x`, net8 consumers were dragged off their own `8.0.x` LTS servicing line. These two packages are now split into per-TFM `ItemGroup`s: `net8.0` floors at the latest stable `8.0.x` (`DependencyInjection.Abstractions` 8.0.2, `Http` 8.0.1), `net10.0` at the latest stable `10.0.x` (both 10.0.10). The `net8.0` dependency group in the `.nupkg` no longer pins net8 apps to a .NET 10 servicing line.
- Bumped `Spectre.Console` (and the test-only `Spectre.Console.Testing`) 0.56.0 → 0.57.2. `Spectre.Console.Cli` stays at 0.55.0 (still its latest stable). These are independently-versioned third-party packages, so they remain a single common `PackageReference` at the built/tested version rather than being split per-TFM.

---

## [0.2.0] — 2026-06-20

### Changed

- **The package now multi-targets `net8.0` alongside `net10.0`** (previously `net10.0` only). .NET 8 CLIs can now consume the library directly instead of being forced onto the latest runtime. No API or behaviour changes — every public surface and feature is identical across both targets, and all dependencies (`Microsoft.Extensions.*`, `Spectre.Console`, `Spectre.Console.Cli`) already ship `net8.0` assets. The produced `.nupkg` carries both `lib/net8.0/` and `lib/net10.0/` folders.

---

## [0.1.10] — 2026-06-10

### Changed

- Bumped NuGet dependencies to their latest stable versions: `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Http` 10.0.8 → 10.0.9, `Spectre.Console` and `Spectre.Console.Testing` 0.55.2 → 0.56.0.
- **Release workflow now uses NuGet Trusted Publishing.** The publish job requests a short-lived API key via `NuGet/login@v1` (GitHub OIDC, `id-token: write`) immediately before `dotnet nuget push`, instead of relying on a long-lived `NUGET_API_KEY` secret.

---

## [0.1.9] — 2026-06-01

### Fixed

- **Prerelease versions with multi-digit numeric identifiers compared as strings, so `rc.10` looked older than `rc.9`.** `ComparePrerelease` used `string.CompareOrdinal`, which orders `"rc.9"` after `"rc.10"` character-by-character (`'9'` > `'1'`). A CLI on `0.5.0-rc.9` running `update --prerelease` against a `0.5.0-rc.10` release therefore reported "Already up to date." Comparison now follows Semantic Versioning §11: dot-separated prerelease identifiers compare left to right, numeric identifiers compare numerically, numeric ranks below alphanumeric, and a longer identifier set outranks a shorter one with an equal prefix.

### Changed

- Bumped NuGet dependencies to their latest versions: `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Http` 10.0.5 → 10.0.8, `Microsoft.SourceLink.GitHub` 8.0.0 → 10.0.300, and the test stack (`Microsoft.NET.Test.Sdk` 17.11.1 → 18.6.0, `xunit` 2.9.2 → 2.9.3, `xunit.runner.visualstudio` 2.8.2 → 3.1.5, `coverlet.collector` 6.0.2 → 10.0.1).

---

## [0.1.8] — 2026-05-27

### Added

- **`UpdateCleanup.Run(...)` startup-cleanup helper.** Startup `CleanupOldInstall()` is synchronous and, under OneDrive / antivirus / Windows Search contention, the `DeleteDirectoryRobustly` retry/backoff path can take several seconds — with no output the app looks hung. New static `UpdateCleanup` helper (mirrors `UpdateBanner`) wraps the cleanup and shows a `Cleaning up previous update…` status spinner **only when there is leftover state to remove**; the common no-leftovers case stays completely silent. `Run(IServiceProvider, IAnsiConsole?)` is the drop-in startup entry point; a `Run(IUpdateInstaller, IAnsiConsole)` overload is provided for explicit/headless callers.
- **`IUpdateInstaller.HasPendingCleanup`.** Cheap, side-effect-free check that returns `true` when a `.old/` or `.update/` directory left by a previous update still exists. Lets a UI layer decide whether to show a cleanup message.

### Changed

- The demo `Program.cs` and the README quick start now call `UpdateCleanup.Run(serviceProvider)` instead of `IUpdateInstaller.CleanupOldInstall()` directly. Purely additive — `CleanupOldInstall()` is unchanged, so existing consumers keep compiling; switching the one startup line opts into the message.

---

## [0.1.7] — 2026-05-25

### Fixed

- **`.update/` staging tree persists across sessions on OneDrive-synced installs.** v0.1.6 wired the recursive-delete-with-retry helper into `InstallAsync`'s end-of-install cleanup, but that pass runs immediately after extraction — the worst possible moment for OneDrive contention, since OneDrive is actively scanning a tree that just appeared. The 1.4 s retry budget loses the race often enough that `.update/<tag>/` accumulates. `CleanupOldInstall` (called at startup) previously only touched `.old/`, so nothing ever retried `.update/` after OneDrive had time to release.
- Fix: `CleanupOldInstall` now cleans both `.old/` and `.update/`. The startup pass is the canonical retry path — by the time the user next launches the CLI, OneDrive has had hours or days to release the handles that defeated the install-time cleanup. Same `DeleteDirectoryRobustly` helper, same swallow-on-final-failure semantics, just a second path. The immediate cleanup in `InstallAsync`'s finally stays as a fast-path for installs where no contention exists.

### Changed

- `IUpdateInstaller.CleanupOldInstall`'s XML doc updated to reflect the broader scope. Method name unchanged for back-compat — consumers' `Program.cs` startup hook keeps working without edits.

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

[0.3.1]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.3.1
[0.3.0]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.3.0
[0.2.0]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.2.0
[0.1.10]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.10
[0.1.9]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.9
[0.1.8]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.8
[0.1.7]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.7
[0.1.6]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.6
[0.1.5]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.5
[0.1.4]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.4
[0.1.3]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.3
[0.1.2]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.2
[0.1.1]: https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/releases/tag/v0.1.1
