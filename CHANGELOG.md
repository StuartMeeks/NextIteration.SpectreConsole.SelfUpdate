# Changelog

All notable changes to `NextIteration.SpectreConsole.SelfUpdate` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

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
