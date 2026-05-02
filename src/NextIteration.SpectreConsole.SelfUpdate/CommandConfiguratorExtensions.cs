using NextIteration.SpectreConsole.SelfUpdate.Commands;
using Spectre.Console.Cli;

namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// Spectre.Console.Cli configurator extensions for registering the
    /// <c>update</c> command surface in a CLI.
    /// </summary>
    public static class CommandConfiguratorExtensions
    {
        /// <summary>
        /// Registers a single top-level <c>update</c> command — the
        /// pl-app-style UX. Mirrors the pl-app reference exactly: <c>-y</c>
        /// /<c>--yes</c> to skip confirmation, <c>--force</c> to reinstall
        /// when already at the latest tag.
        /// </summary>
        /// <param name="configurator">Spectre configurator to add the command to.</param>
        /// <param name="name">Command name. Defaults to <c>"update"</c>.</param>
        public static IConfigurator AddUpdateCommand(this IConfigurator configurator, string name = "update")
        {
            ArgumentNullException.ThrowIfNull(configurator);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            configurator.AddCommand<UpdateCommand>(name)
                .WithDescription("Download and install the latest release from the configured update source.");

            return configurator;
        }

        /// <summary>
        /// Registers an <c>update</c> branch with two subcommands —
        /// <c>check</c> (probe-only) and <c>apply</c> (full install). Use
        /// this when you want to expose the cheap "is there a new version?"
        /// probe as its own user-facing command separate from the install.
        /// </summary>
        /// <param name="configurator">Spectre configurator to add the branch to.</param>
        /// <param name="name">Branch name. Defaults to <c>"update"</c>.</param>
        public static IConfigurator AddUpdateBranch(this IConfigurator configurator, string name = "update")
        {
            ArgumentNullException.ThrowIfNull(configurator);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            configurator.AddBranch(name, branch =>
            {
                branch.SetDescription("Check for and apply CLI updates.");

                branch.AddCommand<UpdateCheckCommand>("check")
                    .WithDescription("Check whether a newer release is available. Does not install.");

                branch.AddCommand<UpdateCommand>("apply")
                    .WithDescription("Download and install the latest release.");
            });

            return configurator;
        }
    }
}
