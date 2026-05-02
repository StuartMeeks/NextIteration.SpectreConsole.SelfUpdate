using System.Globalization;

using Spectre.Console;
using Spectre.Console.Cli;

namespace NextIteration.SpectreConsole.SelfUpdate.Commands
{
    /// <summary>
    /// Probe-only command added by
    /// <see cref="CommandConfiguratorExtensions.AddUpdateBranch"/>.
    /// Reports whether a newer release is available and exits without
    /// touching the install directory. Exit codes:
    /// <list type="bullet">
    ///   <item><description><c>0</c> — up to date.</description></item>
    ///   <item><description><c>1</c> — could not reach the update source.</description></item>
    ///   <item><description><c>2</c> — a newer release is available.</description></item>
    /// </list>
    /// </summary>
    public sealed class UpdateCheckCommand : AsyncCommand
    {
        private readonly IUpdateChecker _checker;
        private readonly IAnsiConsole _console;

        /// <summary>Initializes a new instance from DI.</summary>
        public UpdateCheckCommand(IUpdateChecker checker, IAnsiConsole console)
        {
            ArgumentNullException.ThrowIfNull(checker);
            ArgumentNullException.ThrowIfNull(console);

            _checker = checker;
            _console = console;
        }

        /// <inheritdoc />
        protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            var current = _checker.GetCurrentVersion() ?? "dev";
            _console.MarkupLineInterpolated(CultureInfo.InvariantCulture, $"Current version: [bold]{current}[/]");

            UpdateInfo? info;
            try
            {
                info = await _checker.CheckAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
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

            if (!info.IsUpdateAvailable)
            {
                _console.MarkupLine("[green]Already up to date.[/]");
                return 0;
            }

            _console.MarkupLineInterpolated(CultureInfo.InvariantCulture,
                $"[yellow]Update available:[/] [bold]{info.LatestTag}[/] (you're on [bold]{info.CurrentVersion}[/]).");
            if (info.ReleaseUrl is not null)
            {
                _console.MarkupLineInterpolated(CultureInfo.InvariantCulture, $"Release notes: {info.ReleaseUrl}");
            }
            return 2;
        }
    }
}
