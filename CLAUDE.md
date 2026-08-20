# CLAUDE.md — NextIteration.SpectreConsole.SelfUpdate

## This package

Self-update for Spectre.Console CLIs. A consumer registers an update source —
GitHub Releases over HTTP, GitHub Releases via the `gh` CLI for private repos, a
generic HTTPS manifest, or a custom `IUpdateSource` — and gets a drop-in `update`
command plus an `update check` variant wired into its existing `CommandApp`. The
pipeline resolves the right asset for the running OS/architecture, downloads it,
verifies a SHA-256, expands the archive, and swaps the installed files atomically
with rollback on failure.

Consumed by CLI tools that ship as self-contained directories and replace
themselves in place. The install path is the part that matters: a failed update
must leave a working application behind, not a half-written one.

## Things that are easy to get wrong here

- **The three-platform test matrix is load-bearing, not ceremony.** The library
  resolves an OS/arch RID token, picks a per-OS cache directory (`AppData`,
  `~/Library/Caches`, `XDG_CACHE_HOME`), replaces a *running* executable, and
  takes an install lock whose `FileShare.None` + `FileOptions.DeleteOnClose`
  semantics differ between POSIX and Windows. Dropping a leg stops testing a
  shipped code path. `InstallLockTests.Acquire_when_directory_not_writable_throws_not_writable`
  returns early on Windows *by design* — read its comment before "fixing" it; a
  platform-guarded test passes vacuously off its platform.
- **SHA-256 verification proves integrity, not authenticity.** The expected hash
  comes from the same release as the asset. Do not describe it, in docs or in
  XML comments, as protecting against a malicious publisher — see `SECURITY.md`
  for the boundary that is actually claimed.
- **`AllowInsecureManifestSource` and `UseDefaultSha256Verifier=false` are
  deliberate opt-outs**, documented as tests/trusted-network only. Do not
  "harden" them away; do not widen where they apply.
- **Per-TFM dependency floors are deliberate.** `Microsoft.Extensions.Http` and
  `Microsoft.Extensions.DependencyInjection.Abstractions` are floored at 8.0.x
  for `net8.0` and 10.0.x for `net10.0`. Raising the net8 floor to a 10.x version
  drags every net8 LTS consumer off its own servicing line. Dependabot is
  configured never to propose it; do not do it by hand either.
- **`PackageValidationBaselineVersion` is set to the last shipped release.** An
  accidental public-API break fails the build rather than shipping. When the
  version is bumped for a release, the baseline moves with it — not before.
- **Cleanup must stay silent when there is nothing to clean.** `UpdateCleanup`
  shows a status message only when `HasPendingCleanup` is true, because it runs at
  the very start of every `Main` and the no-leftovers case is the common one.
- **The demo project is not shipped** (`IsPackable=false`, `net10.0` only) but it
  is in the solution and it builds in CI, so it must compile warning-free like
  everything else.

## Repository baseline

This repo conforms to
[NextIteration.Standards](https://github.com/StuartMeeks/NextIteration.Standards).
Build properties, test stack, CI shape, and branch protection are defined there, not
here. Before changing any of those, read `STANDARD.md`; if this repo needs to deviate,
that is an `EXCEPTIONS.md` entry in the standards repo, not a local difference.

## Non-negotiables

- **The build must be clean.** `TreatWarningsAsErrors` is on and analyzers run at
  `latest`. A warning is a build failure.
- **Tests must pass on every shipped target framework** (`net8.0` and `net10.0`). A change
  that only passes on one is not finished. Shipping a target you do not test is a defect,
  not a scoping decision.
- **Dependency floors are deliberate and per-TFM.** A `PackageReference` version in a
  library is a *minimum* NuGet forces on every consumer, so raising a floor is a
  consumer-visible change even when nothing in the code needs it. Never raise one to
  silence a warning.
- **Public API changes need XML docs.** `GenerateDocumentationFile` is on and the public
  surface is fully documented.
- **Update `CHANGELOG.md`** under `[Unreleased]`, saying what changed and why.

## Dependabot

Minor and patch updates auto-merge behind CI. Major updates stay open for a human — that
is deliberate, not a backlog to clear. Packages with per-TFM floors have major updates
suppressed entirely via `ignore`; bump those by hand when a new .NET major lands.

## After opening a pull request

Watch CI to completion, report the real check results, then **offer to merge** in the same
message. Do not stop silently and wait to be asked.

- If branch protection blocks the merge, say so and offer `gh pr merge --admin`. These
  repos require a code-owner review only the maintainer can give, which is why `--admin` is
  the tool — but that mechanic is not the reason the offer is wanted. The reason is simply
  that the maintainer has grown comfortable delegating this to an agent, so treat the
  latest instruction as authoritative over this file.
- **Merge only on an explicit yes.** The offer is pre-approved; the action is not.
- Never offer while checks are failing or still running. Report that state instead.
- Report the checks that actually ran. A skipped check is not a passing check, and branch
  protection treats them differently from how they read in a summary.

## CI

The single required status check is `ci` — an aggregating gate over `build` and `test`.
Renaming those jobs is safe; the ruleset never names them. Do not make them required
checks directly.

Publishing lives in `ci.yml`, not a separate `release.yml` — and the nuget.org Trusted
Publishing policy is bound to that *filename*. Renaming the workflow file requires
updating the policy in the same change, or the next publish fails to authenticate.
