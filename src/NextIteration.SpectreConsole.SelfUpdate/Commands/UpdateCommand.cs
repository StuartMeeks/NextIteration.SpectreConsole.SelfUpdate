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
        }

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

            UpdateInfo? info;
            try
            {
                info = await _checker.CheckAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _console.MarkupLineInterpolated(CultureInfo.InvariantCulture,
                    $"[red]Could not determine the latest release:[/] {ex.Message}");
                return 1;
            }

            if (info is null)
            {
                _console.MarkupLine("[red]Could not determine the latest release. The update source returned no result.[/]");
                return 1;
            }

            _console.MarkupLineInterpolated(CultureInfo.InvariantCulture, $"Latest release: [bold]{info.LatestTag}[/]");

            if (!settings.Force && !info.IsUpdateAvailable)
            {
                _console.MarkupLine("[green]Already up to date.[/]");
                return 0;
            }

            if (!settings.Yes && !PromptToContinue(info))
            {
                _console.MarkupLine("Aborted.");
                return 2;
            }

            try
            {
                await _console.Status().StartAsync("Updating…", async statusContext =>
                {
                    var progress = new Progress<UpdateProgressEvent>(evt =>
                        statusContext.Status(StageLabel(evt.Stage)));
                    await _selfUpdater.InstallAsync(progress, cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            catch (UpdateException ex)
            {
                _console.MarkupLineInterpolated(CultureInfo.InvariantCulture, $"[red]Update failed:[/] {ex.Message}");
                return 3;
            }

            _console.MarkupLineInterpolated(CultureInfo.InvariantCulture,
                $"[green]Installed [bold]{info.LatestTag}[/]. Re-run the CLI to use the new version.[/]");
            return 0;
        }

        private bool PromptToContinue(UpdateInfo info)
        {
            return _console.Confirm(
                $"Install [bold]{info.LatestTag}[/] over the current install in {Markup.Escape(_installer.InstallDirectory)}?",
                defaultValue: true);
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
