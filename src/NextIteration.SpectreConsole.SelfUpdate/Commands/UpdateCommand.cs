using System.ComponentModel;
using System.Globalization;

using Spectre.Console;
using Spectre.Console.Cli;

namespace NextIteration.SpectreConsole.SelfUpdate.Commands
{
    /// <summary>
    /// Spectre.Console.Cli command that downloads the latest release from
    /// the configured update source, verifies it, and swaps it into the
    /// current install directory. Exit codes:
    /// <list type="bullet">
    ///   <item><description><c>0</c> — installed, or already up to date.</description></item>
    ///   <item><description><c>1</c> — could not reach the update source.</description></item>
    ///   <item><description><c>2</c> — user cancelled at the confirmation prompt.</description></item>
    ///   <item><description><c>3</c> — update failed during download / verify / swap.</description></item>
    /// </list>
    /// </summary>
    public sealed class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
    {
        /// <summary>Settings for <c>update</c>.</summary>
        public sealed class Settings : CommandSettings
        {
            /// <summary>Skip the confirmation prompt.</summary>
            [CommandOption("-y|--yes")]
            [Description("Skip the confirmation prompt and proceed.")]
            public bool Yes { get; init; }

            /// <summary>Reinstall the latest version even if already at it.</summary>
            [CommandOption("--force")]
            [Description("Reinstall even if already on the latest version.")]
            public bool Force { get; init; }

            /// <summary>
            /// How to resolve conflicts where a new release ships an entry whose
            /// path matches one of <c>SelfUpdaterOptions.PreservePaths</c>:
            /// <c>ask</c> prompts per file, <c>keep</c> always keeps the user's,
            /// <c>new</c> always uses the new release's. Default <c>ask</c> when
            /// running interactively (no <c>--yes</c>), <c>keep</c> with <c>--yes</c>.
            /// </summary>
            [CommandOption("--strategy")]
            [Description("Conflict strategy when a release ships a preserved path: ask | keep | new.")]
            public string? Strategy { get; init; }

            /// <summary>
            /// Opt into prerelease tags for this invocation only. Equivalent
            /// to flipping <c>SelfUpdaterOptions.IncludePrereleases</c> to
            /// <c>true</c> for the lifetime of this command without mutating
            /// shared options.
            /// </summary>
            [CommandOption("--prerelease")]
            [Description("Consider GitHub prereleases when looking for the latest version (off by default).")]
            public bool Prerelease { get; init; }
        }

        private enum ConflictStrategy { Ask, Keep, New }

        private readonly ISelfUpdater _selfUpdater;
        private readonly IUpdateChecker _checker;
        private readonly IUpdateInstaller _installer;
        private readonly IAnsiConsole _console;

        /// <summary>Initializes a new instance from DI.</summary>
        public UpdateCommand(
            ISelfUpdater selfUpdater,
            IUpdateChecker checker,
            IUpdateInstaller installer,
            IAnsiConsole console)
        {
            ArgumentNullException.ThrowIfNull(selfUpdater);
            ArgumentNullException.ThrowIfNull(checker);
            ArgumentNullException.ThrowIfNull(installer);
            ArgumentNullException.ThrowIfNull(console);

            _selfUpdater = selfUpdater;
            _checker = checker;
            _installer = installer;
            _console = console;
        }

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var current = _checker.GetCurrentVersion() ?? "dev";
            _console.MarkupLineInterpolated(CultureInfo.InvariantCulture, $"Current version: [bold]{current}[/]");

            // Fetch the release once and use it for both display and install,
            // so the user confirms exactly the release that gets installed
            // (no TOCTOU window between "what's latest?" and "install latest").
            bool? prereleaseOverride = settings.Prerelease ? true : null;
            RemoteRelease? release;
            try
            {
                release = await _selfUpdater.GetLatestReleaseAsync(prereleaseOverride, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _console.MarkupLineInterpolated(CultureInfo.InvariantCulture,
                    $"[red]Could not determine the latest release:[/] {ex.Message}");
                return 1;
            }

            if (release is null)
            {
                _console.MarkupLine("[red]Could not determine the latest release. The update source returned no result.[/]");
                return 1;
            }

            _console.MarkupLineInterpolated(CultureInfo.InvariantCulture, $"Latest release: [bold]{release.Tag}[/]");

            if (!settings.Force && !IsUpdateAvailable(current, release.Tag))
            {
                _console.MarkupLine("[green]Already up to date.[/]");
                return 0;
            }

            if (!settings.Yes && !PromptToContinue(release.Tag))
            {
                _console.MarkupLine("Aborted.");
                return 2;
            }

            var strategy = ResolveStrategy(settings);
            var conflictResolver = BuildConflictResolver(strategy);

            try
            {
                await _console.Status().StartAsync("Updating…", async statusContext =>
                {
                    var progress = new Progress<UpdateProgressEvent>(evt =>
                        statusContext.Status(StageLabel(evt.Stage)));
                    await _selfUpdater.InstallAsync(release, progress, conflictResolver, cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            catch (UpdateException ex)
            {
                _console.MarkupLineInterpolated(CultureInfo.InvariantCulture, $"[red]Update failed:[/] {ex.Message}");
                return 3;
            }

            _console.MarkupLineInterpolated(CultureInfo.InvariantCulture,
                $"[green]Installed [bold]{release.Tag}[/]. Re-run the CLI to use the new version.[/]");
            return 0;
        }

        private bool PromptToContinue(string tag)
        {
            return _console.Confirm(
                $"Install [bold]{tag}[/] over the current install in {Markup.Escape(_installer.InstallDirectory)}?",
                defaultValue: true);
        }

        private static ConflictStrategy ResolveStrategy(Settings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Strategy))
            {
                // Non-interactive runs (--yes) default to keep so updates
                // never block on a prompt. Interactive runs default to ask
                // so the user gets a real chance to make a decision.
                return settings.Yes ? ConflictStrategy.Keep : ConflictStrategy.Ask;
            }
            return settings.Strategy.ToLowerInvariant() switch
            {
                "ask" => ConflictStrategy.Ask,
                "keep" => ConflictStrategy.Keep,
                "new" => ConflictStrategy.New,
                _ => throw new InvalidOperationException(
                    $"Unknown --strategy value '{settings.Strategy}'. Expected one of: ask, keep, new."),
            };
        }

        private Func<UpdateConflict, CancellationToken, Task<UpdateConflictResolution>>? BuildConflictResolver(ConflictStrategy strategy) =>
            strategy switch
            {
                ConflictStrategy.Keep => null,   // null is the documented "KeepExisting" default
                ConflictStrategy.New => (_, _) => Task.FromResult(UpdateConflictResolution.UseNew),
                ConflictStrategy.Ask => PromptForConflictAsync,
                _ => null,
            };

        private Task<UpdateConflictResolution> PromptForConflictAsync(UpdateConflict conflict, CancellationToken ct)
        {
            _console.MarkupLineInterpolated(CultureInfo.InvariantCulture,
                $"[yellow]Preserved path conflict:[/] [bold]{Markup.Escape(conflict.RelativePath)}[/]");
            if (conflict.ExistingSizeBytes is { } ex && conflict.NewSizeBytes is { } nu)
            {
                _console.MarkupLineInterpolated(CultureInfo.InvariantCulture,
                    $"  yours: {ex} bytes  →  new: {nu} bytes");
            }
            var keep = _console.Confirm("Keep your existing file?", defaultValue: true);
            return Task.FromResult(keep ? UpdateConflictResolution.KeepExisting : UpdateConflictResolution.UseNew);
        }

        private static bool IsUpdateAvailable(string current, string latestTag)
        {
            // Defer to the same comparator the checker uses so behaviour is
            // identical between the cached probe and this fresh one.
            return Pipeline.UpdateChecker.IsNewer(current, latestTag);
        }

        private static string StageLabel(UpdateStage stage) => stage switch
        {
            UpdateStage.Downloading => "Downloading…",
            UpdateStage.Verifying => "Verifying checksum…",
            UpdateStage.Extracting => "Extracting…",
            UpdateStage.Swapping => "Swapping files…",
            UpdateStage.CleaningUp => "Cleaning up…",
            _ => "Updating…",
        };
    }
}
