# NextIteration.SpectreConsole.SelfUpdate

[![NuGet](https://img.shields.io/nuget/v/NextIteration.SpectreConsole.SelfUpdate.svg)](https://www.nuget.org/packages/NextIteration.SpectreConsole.SelfUpdate/)
[![Downloads](https://img.shields.io/nuget/dt/NextIteration.SpectreConsole.SelfUpdate.svg)](https://www.nuget.org/packages/NextIteration.SpectreConsole.SelfUpdate/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![CI](https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/actions/workflows/ci.yml/badge.svg)](https://github.com/StuartMeeks/NextIteration.SpectreConsole.SelfUpdate/actions/workflows/ci.yml)

Self-update for CLI tools built on [Spectre.Console](https://spectreconsole.net/) — check for new releases, download, verify, and atomically swap the install in place. Pluggable update sources (public GitHub Releases over HTTP, private repos via the `gh` CLI, generic HTTPS manifest, your own).

Stop hand-rolling the same "where's the latest release / what's my RID / where do I write the new EXE" code into every CLI you ship. Drop this package in, register a source, and `my-cli update` just works — with SHA-256 verification, an atomic file swap, a 24h read-through cache, and a magenta "new version available" banner that costs nothing on the warm path.

---

## Features

- **Three built-in update sources, one extension point.** Public GitHub Releases (HTTP), private GitHub Releases (via the `gh` CLI), generic HTTPS JSON manifest, or implement `IUpdateSource` for anything else.
- **Asset resolution that just works.** Format-agnostic resolver picks the right `.zip` / `.tar.gz` for the running RID with sensible fallbacks (Apple Silicon → Rosetta → universal).
- **SHA-256 verification by default.** Reads from per-asset metadata when the source publishes it, or downloads a sibling `SHA256SUMS.txt`. Stack additional verifiers (minisign, cosign, Authenticode) on top via `IPackageVerifier`.
- **Atomic file swap, no restarter required.** Stages under `.update/`, swaps via `.old/`, cleans up `.old/` on the next launch — the running new binary is sufficient proof the swap succeeded.
- **Drop-in `update` command.** `CommandConfiguratorExtensions.AddUpdateCommand()` for a single command, `AddUpdateBranch()` for `update check` / `update apply`.
- **Background check + post-run banner.** `UpdateBanner.KickOffCheck()` and `RenderIfAvailable()` wire up the pl-app-style UX in two lines.
- **Channels & pre-releases.** `Channel` option flows through every source.
- **Configurable cache, timeouts, opt-out env var, dev-build skip predicate.**
- **Zero compiler warnings, full XML documentation on the public surface**, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest`. SourceLink, deterministic builds, embedded symbols, snupkg symbol packages.

---

## Install

```shell
dotnet add package NextIteration.SpectreConsole.SelfUpdate
```

## Quick start

```csharp
var services = new ServiceCollection();
services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);

services.AddSelfUpdater(opts =>
{
    opts.AppName = "myapp";
    opts.UseGitHubReleases("acme/myapp");
});

using var sp = services.BuildServiceProvider();
UpdateCleanup.Run(sp);   // sweeps .old/.update; shows a message only if there's leftover state
var checkTask = UpdateBanner.KickOffCheck(sp);

var app = new CommandApp(new YourTypeRegistrar(sp));
app.Configure(c =>
{
    c.SetApplicationName("myapp");
    c.AddUpdateCommand();          // adds `myapp update`
});
var exit = app.Run(args);

UpdateBanner.RenderIfAvailable(checkTask);
return exit;
```

A complete demo (including a small `ServiceProviderTypeRegistrar` you can copy verbatim) lives in [`demo/`](demo/NextIteration.SpectreConsole.SelfUpdate.Demo/Program.cs).

---

## Update sources

| Source | Use it when | Configure |
|---|---|---|
| `HttpGitHubReleaseSource` (default) | Your repo is public, or you can supply a `GITHUB_TOKEN`. | `opts.UseGitHubReleases("owner/repo")` |
| `GhCliReleaseSource` | Your repo is private and your users have `gh auth` set up. | `opts.UseGitHubReleases("owner/repo", GitHubTransport.GhCli)` |
| `HttpManifestSource` | You publish releases to S3 / Azure Blob / a static site / your own backend. | `opts.UseHttpManifest(new Uri("https://example.com/latest.json"))` |
| Custom `IUpdateSource` | Anything else — internal artifact registry, signed CDN, etc. | `opts.UseSource<MySource>()` or `opts.UseSource(sp => new MySource(...))` |

### HTTPS manifest schema

`HttpManifestSource` expects a JSON document with this shape — drop it on any HTTPS endpoint:

```json
{
  "tag": "v1.4.2",
  "channel": "stable",
  "publishedAt": "2026-04-30T12:00:00Z",
  "releaseNotesUrl": "https://example.com/notes/v1.4.2",
  "assets": [
    {
      "name": "myapp-v1.4.2-linux-x64.tar.gz",
      "url":  "https://example.com/dl/myapp-v1.4.2-linux-x64.tar.gz",
      "sizeBytes": 12345678,
      "contentType": "application/gzip",
      "sha256": "abc123...64hex...def456"
    }
  ]
}
```

When `sha256` is populated, the default verifier picks it up via `ReleaseAsset.Metadata` — no separate `SHA256SUMS.txt` needed.

### Custom update sources

```csharp
public sealed class MyArtifactRegistrySource : IUpdateSource
{
    public Task<RemoteRelease?> GetLatestAsync(string? channel, CancellationToken ct) { /* ... */ }
    public Task DownloadAssetAsync(ReleaseAsset asset, Stream destination,
                                   IProgress<DownloadProgress>? progress, CancellationToken ct) { /* ... */ }
}

services.AddSelfUpdater(opts =>
{
    opts.AppName = "myapp";
    opts.UseSource<MyArtifactRegistrySource>();
});
```

The contract is intentionally tiny: "what's the latest release?" and "stream me an asset". Everything else (asset resolution, verification, atomic swap, the `update` command) lives in the package.

---

## Asset resolution

The default resolver picks the archive whose filename matches the running RID. The expected naming convention is:

```
{app}-v{version}-{rid}.(zip|tar.gz|tgz)
```

Examples that all work without configuration: `myapp-v1.4.2-linux-x64.tar.gz`, `myapp-1.4.2-osx-arm64.zip`, `myapp-osx-arm64.zip`. Matching is case-insensitive, format-agnostic, and falls back through unversioned and rid-only patterns. `osx-arm64` falls back to `osx-x64` (Rosetta) and then bare `osx` (universal).

Override for releases with non-standard names:

```csharp
opts.UseAssetResolver((release, rid) =>
    release.Assets.FirstOrDefault(a => a.Name.Contains(rid) && a.Name.EndsWith(".zip")));
```

**Archive-only in v0.1.** The atomic swap pipeline assumes a directory of files; single-file executables (`myapp-linux-x64`, no extension) are not currently supported. Ship a `.zip` or `.tar.gz` containing the executable and any sidecar files.

---

## SHA-256 verification

A SHA-256 verifier runs by default. It looks for a hash in two places, in order:

1. The asset's `Metadata["sha256"]` entry — populated automatically by `HttpManifestSource` when the manifest publishes per-asset hashes.
2. A sibling `SHA256SUMS.txt` (also accepts `SHA256SUMS`, `sha256sums.txt`, `sha256sums`, `checksums.txt`) — downloaded via the registered `IUpdateSource` and parsed line-by-line.

If neither is present, the verifier throws — downloading without verification is unsafe. To opt out:

```csharp
opts.UseDefaultSha256Verifier = false;
```

To stack additional verifiers (signature checking, key pinning, your own policy), add them via `AddVerifier`:

```csharp
opts.AddVerifier<MinisignVerifier>();   // type, resolved from DI
opts.AddVerifier(sp => new MyKeyPinningVerifier(sp.GetRequiredService<HttpClient>()));
```

All verifiers are invoked in registration order; any one of them throwing aborts the install before extraction.

---

## Channels & pre-releases

Set `opts.Channel = "beta"` and the source will look for tags containing `-beta` (the de-facto convention for SemVer prerelease tags). Set `opts.IncludePrereleases = true` to consider any prerelease — useful when you publish prereleases without a channel suffix.

For sources that don't have a channel concept (`HttpManifestSource`), host one manifest per channel — `releases/stable/latest.json`, `releases/beta/latest.json` — and configure the appropriate URL.

---

## How the atomic swap works

```
<install>/
├── myapp.exe                    # current install
├── settings.json
├── .update/                     # transient; created during install
│   └── v1.4.2/                  #   staging dir for one specific tag
│       ├── myapp-v1.4.2-...-x64.zip
│       └── extracted/
│           └── myapp-v1.4.2-x64/
│               ├── myapp.exe    # the new files
│               └── settings.json
├── .update.lock                 # transient; mutex during install
└── .old/                        # transient; previous install
    ├── myapp.exe                #   moved here during the swap
    └── settings.json            #   deleted on next startup
```

1. **Acquire lock.** `<install>/.update.lock` opened with `FileShare.None` + `FileOptions.DeleteOnClose`. Concurrent installs lose the race with a clear "another update is in progress" message.
2. **Stage.** Download the asset under `<install>/.update/<tag>/`, run every registered `IPackageVerifier`, extract.
3. **Swap.** Move every entry in `<install>/` (except the maintenance dirs) into `<install>/.old/`. Copy the extracted files into place. Delete `<install>/.update/`.
4. **Cleanup later.** Next startup, `IUpdateInstaller.CleanupOldInstall()` deletes `<install>/.old/` (and any leftover `<install>/.update/`) — the running new binary is proof the swap completed. Call it via `UpdateCleanup.Run(sp)`, which shows a "cleaning up" status message while it works but only when there is leftover state to remove.

This avoids the "EXE is locked while running" problem on Windows without a separate restarter process.

---

## Preserving user files across updates

By default the installer treats every entry in the install directory as
package-owned: anything that's not in the new release ends up in `.old/`
and gets cleaned up on next startup. That's wrong for files the *user*
placed there — `appsettings.Development.json`, a local SQLite database,
a `data/` folder, scratch notes, plugins.

`SelfUpdaterOptions.PreservePaths` is a list of glob patterns identifying
top-level entries the installer should leave alone:

```csharp
services.AddSelfUpdater(opts =>
{
    opts.AppName = "myapp";
    opts.UseGitHubReleases("acme/myapp");

    opts.PreservePaths = new[]
    {
        "appsettings.Development.json",   // exact filename
        "appsettings.*.json",             // glob over top-level files
        "data/**",                        // whole top-level directory
        "*.db",                           // sqlite or similar
    };
});
```

> **One-time gotcha when adopting `PreservePaths`.** This option was
> introduced in v0.1.3. The first time a user upgrades *from* a
> pre-0.1.3 build of your CLI *to* a 0.1.3+ build, the swap is
> performed by the **old** binary's update code — which doesn't know
> about `PreservePaths` and will move every file (including the ones
> you intend to preserve) into `.old/`, where they're wiped on the
> next launch. From the *next* update onwards (a 0.1.3+ binary
> upgrading to a newer 0.1.3+ binary), preservation works as
> documented. If your users already have important state in the
> install directory before this change ships, document a one-shot
> manual backup step or have your CLI bootstrap missing files on
> first launch after the upgrade.

Patterns match the **top-level entry name** (the part before the first
`/` in the pattern). `data/**` and `data/seed.json` both preserve the
whole `data/` directory; nested-only preservation isn't supported in
v0.1.x.

### Letting end users extend the list via `appsettings.json`

The `PreservePaths` property is a plain `IReadOnlyList<string>`, so
consumers can populate it from `IConfiguration` and merge with their
in-code defaults:

```csharp
var fromConfig = configuration
    .GetSection("SelfUpdate:Preserve")
    .Get<string[]>() ?? Array.Empty<string>();

services.AddSelfUpdater(opts =>
{
    opts.AppName = "myapp";
    opts.UseGitHubReleases("acme/myapp");
    opts.PreservePaths = new[]
    {
        "appsettings.Development.json",
        "data/**",
    }.Concat(fromConfig).ToArray();
});
```

End users can then drop their own paths into `appsettings.json` (or
`appsettings.Development.json` next to the binary) without recompiling
the CLI:

```json
{
  "SelfUpdate": {
    "Preserve": [ "my-custom-plugins/**", "notes.md" ]
  }
}
```

### Conflict resolution when a release ships a preserved path

If a new release publishes an entry whose path matches one of the
preserve patterns, the installer asks the resolver passed to
`InstallAsync` what to do:

```csharp
await selfUpdater.InstallAsync(
    release,
    progress: null,
    onConflict: (conflict, ct) =>
    {
        // conflict.RelativePath, conflict.ExistingSizeBytes, conflict.NewSizeBytes
        return Task.FromResult(UpdateConflictResolution.KeepExisting);
    });
```

Default (no resolver passed) is `KeepExisting` — the user's file wins.
The drop-in `update` command exposes a `--strategy ask|keep|new` flag:
with `--yes` it defaults to `keep` (so unattended runs never block on a
prompt); without `--yes` it defaults to `ask` and prompts the user
per file via Spectre's `Confirm`.

---

## Working with `appsettings.json` across updates

A common question when shipping a CLI: "users edit `appsettings.json` to
set their connection strings — how do I keep their edits without
losing the new options I add in a new release?"

**The package's strong recommendation: don't try to merge.** Use the
.NET layering convention instead, where `appsettings.json` is *defaults
shipped by the package* and `appsettings.{Environment}.json` (or
`appsettings.Local.json`, environment variables, Azure App
Configuration, etc.) is *the user's overrides*. `IConfiguration`
overlays them at runtime — the merge happens in memory, with no file
on disk to reconcile.

With that convention:

```csharp
opts.PreservePaths = new[]
{
    "appsettings.Development.json",   // user-owned overrides — preserve
    "appsettings.Local.json",         // user-owned overrides — preserve
    // appsettings.json is package-owned — let new releases update it
};
```

A user who currently has connection strings in `appsettings.json`
should move them to `appsettings.Local.json` (or set
`ASPNETCORE_ENVIRONMENT` and use `appsettings.{Environment}.json`).
One-time migration; zero ongoing complexity. The base
`appsettings.json` keeps shipping new defaults and feature toggles
without ever clobbering user config.

### When you really do need to merge two JSON files

If you can't move users to layered overrides — say a third-party tool
expects `appsettings.json` to be hand-edited — the `onConflict`
resolver is enough of an escape hatch to do a surgical merge. Keep
`appsettings.json` in `PreservePaths`, then in the resolver: read both
files, merge them however you like, write the result to the install
directory, and return `KeepExisting` so the installer leaves the
freshly-merged file alone:

```csharp
opts.PreservePaths = new[] { "appsettings.json" };

await selfUpdater.InstallAsync(release, progress, onConflict: (conflict, ct) =>
{
    if (conflict.RelativePath == "appsettings.json")
    {
        var existingPath = Path.Combine(installer.InstallDirectory, "appsettings.json");
        var newPath      = Path.Combine(stagingForThisRun, "appsettings.json");

        // Bring your own merger — JsonNode walk, JObject.Merge, whatever
        // matches your file's shape. Comments and formatting are
        // typically lost on a JSON round-trip; consider that a reason
        // to prefer layered overrides instead.
        var merged = MergeJson(existingPath, newPath);
        File.WriteAllText(existingPath, merged);

        // Tell the installer "I've handled it — don't overwrite".
        return Task.FromResult(UpdateConflictResolution.KeepExisting);
    }
    return Task.FromResult(UpdateConflictResolution.KeepExisting);
});
```

A few caveats worth knowing:

- **Without a base version, you can't tell user-modified from
  unchanged.** If the user has `LogLevel.Default = "Warning"` and the
  new release ships `"Information"`, was the user being deliberate or
  did they just inherit the previous default? A robust merger would
  need to persist the previous release's `appsettings.json` somewhere
  to use as a diff base.
- **JSON round-trips drop comments and reformat.** Consumers who use
  inline comments in their config will notice.
- **No built-in merger ships with this package.** Config-file
  semantics are app-specific (deep-merge vs replace, array-append vs
  array-replace, …) and end up being a tar pit. The package stops at
  "preserve or overwrite per file"; merging policy belongs in the
  consumer.

---

## Configuration reference

| Property | Default | Description |
|---|---|---|
| `AppName` | (required) | Logical CLI name. Used for cache dir, env-var name, and asset resolution. |
| `Channel` | `null` | Release-channel filter. `null` = source default. |
| `IncludePrereleases` | `false` | When true, prerelease tags are eligible for "latest". |
| `CacheTtl` | `24h` | How long the cached "latest tag" answer stays fresh. |
| `CheckTimeout` | `3s` | Maximum time the background check spends talking to the source. |
| `DownloadTimeout` | `5min` | Maximum time an asset download is allowed to take. |
| `CacheDirectory` | per-platform default | `%APPDATA%/<app>/`, `~/Library/Caches/<app>/`, or `$XDG_CACHE_HOME/<app>/`. |
| `SkipCheckEnvironmentVariable` | `<APP>_SKIP_UPDATE_CHECK` | Env var that suppresses the check when set to `1`. |
| `SkipVersionPredicate` | `v => v == "1.0.0"` | Returns true to suppress the check (for unstamped dev builds). |
| `GitHubToken` | `null` (then `GITHUB_TOKEN`/`GH_TOKEN` env) | Optional bearer token for `HttpGitHubReleaseSource`. |
| `UseDefaultSha256Verifier` | `true` | Whether to register the SHA-256 verifier automatically. |
| `AllowInsecureManifestSource` | `false` | When true, `HttpManifestSource` accepts `http://` URLs. Tests / internal mirrors only — plain HTTP defeats SHA-256 verification. |
| `PreservePaths` | `[]` | Glob patterns identifying top-level entries the installer must leave alone (`appsettings.Development.json`, `data/**`, `*.db`, …). See [Preserving user files](#preserving-user-files-across-updates). |

---

## Architecture

| Layer | Purpose | Default impl | Override |
|---|---|---|---|
| `IUpdateSource` | "What's the latest release? Stream me an asset." | one of the three built-ins, by configuration | `opts.UseSource<T>()` / `UseSource(factory)` |
| `IAssetResolver` | "Pick the right asset for the running RID." | `DefaultAssetResolver` | `opts.UseAssetResolver(...)` |
| `IPackageVerifier` | "Confirm the bytes are what they claim to be." (multi-instance) | `Sha256ChecksumVerifier` | `opts.AddVerifier<T>()`, set `UseDefaultSha256Verifier=false` to opt out |
| `IUpdateChecker` | "Is there a newer release?" with caching + opt-out | `UpdateChecker` | replace via DI |
| `IUpdateInstaller` | Stage / verify / extract / swap / cleanup pipeline | `UpdateInstaller` | replace via DI |
| `ISelfUpdater` | High-level façade for consumers | `SelfUpdater` | replace via DI |

Every layer is pluggable. The contracts are intentionally small so a custom implementation only has to do the work that's actually different — the rest of the package keeps working.

---

## License

MIT — © 2026 Stuart Meeks
