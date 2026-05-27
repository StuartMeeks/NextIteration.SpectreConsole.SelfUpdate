using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;

namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// One-call helper for the startup cleanup of a previous update's
    /// <c>.old/</c> and <c>.update/</c> leftovers. Call <see cref="Run(IServiceProvider, IAnsiConsole?)"/>
    /// at the very start of <c>Main</c> in place of a bare
    /// <see cref="IUpdateInstaller.CleanupOldInstall"/> call.
    /// <para>
    /// The delete is synchronous and — under OneDrive / antivirus / Windows
    /// Search contention — can take seconds (see the retry/backoff path in
    /// the installer). Without feedback the app looks hung. This helper shows
    /// a status message while it works, but <b>only</b> when there is actually
    /// something to clean (<see cref="IUpdateInstaller.HasPendingCleanup"/>);
    /// the common no-leftovers case stays completely silent.
    /// </para>
    /// </summary>
    public static class UpdateCleanup
    {
        /// <summary>
        /// Status text shown while leftover update state is being removed.
        /// </summary>
        public const string DefaultMessage = "Cleaning up previous update…";

        /// <summary>
        /// Resolve <see cref="IUpdateInstaller"/> from the supplied service
        /// provider and run the startup cleanup, showing
        /// <see cref="DefaultMessage"/> only when leftover state exists.
        /// </summary>
        /// <param name="services">DI container holding the registered <see cref="IUpdateInstaller"/>.</param>
        /// <param name="console">
        /// Optional console override. When <see langword="null"/>, an
        /// <see cref="IAnsiConsole"/> registered in <paramref name="services"/>
        /// is used; failing that, <see cref="AnsiConsole.Console"/>.
        /// </param>
        public static void Run(IServiceProvider services, IAnsiConsole? console = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            var installer = services.GetRequiredService<IUpdateInstaller>();
            Run(installer, console ?? services.GetService<IAnsiConsole>() ?? AnsiConsole.Console);
        }

        /// <summary>
        /// Run the startup cleanup against an explicit installer and console.
        /// Shows <see cref="DefaultMessage"/> only when
        /// <see cref="IUpdateInstaller.HasPendingCleanup"/> is
        /// <see langword="true"/>; otherwise cleans silently.
        /// </summary>
        /// <param name="installer">The installer to clean up.</param>
        /// <param name="console">Console used to render the status message.</param>
        public static void Run(IUpdateInstaller installer, IAnsiConsole console)
        {
            ArgumentNullException.ThrowIfNull(installer);
            ArgumentNullException.ThrowIfNull(console);

            if (!installer.HasPendingCleanup)
            {
                installer.CleanupOldInstall();   // nothing to clean — stay silent
                return;
            }

            // The spinner communicates "in progress" while the synchronous,
            // retry-backed delete runs, then clears when it returns.
            console.Status().Start(DefaultMessage, _ => installer.CleanupOldInstall());
        }
    }
}
