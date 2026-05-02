using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;

namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// One-call helpers for the "background check + post-run banner" UX.
    /// Mirrors the pl-app reference: <see cref="KickOffCheck"/> at the
    /// start of <c>Main</c>, <see cref="RenderIfAvailable"/> after
    /// <c>app.Run</c> returns. The check is short-timeout and
    /// read-through-cached, so it costs nothing on the happy path.
    /// </summary>
    public static class UpdateBanner
    {
        /// <summary>
        /// Default time the banner renderer waits for the background check
        /// to settle before giving up. 250ms — enough for a warm cache,
        /// short enough to never feel sluggish.
        /// </summary>
        public static readonly TimeSpan DefaultRenderWaitFor = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Resolve <see cref="ISelfUpdater"/> from the supplied service
        /// provider and kick off
        /// <see cref="ISelfUpdater.CheckAsync"/> without blocking.
        /// </summary>
        /// <param name="services">DI container holding the registered <see cref="ISelfUpdater"/>.</param>
        /// <returns>The in-flight task — pass it to <see cref="RenderIfAvailable"/> later.</returns>
        public static Task<UpdateInfo?> KickOffCheck(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);
            var updater = services.GetRequiredService<ISelfUpdater>();
            return updater.CheckAsync();
        }

        /// <summary>
        /// Wait briefly for <paramref name="checkTask"/> to complete and
        /// render a one-line banner if an update is available. No-op if
        /// the task hasn't completed within <paramref name="waitFor"/>, if
        /// it returned <see langword="null"/>, or if no update is available.
        /// </summary>
        /// <param name="checkTask">The task returned by <see cref="KickOffCheck"/>.</param>
        /// <param name="waitFor">Maximum time to wait. Defaults to 250 ms.</param>
        /// <param name="console">Optional console override; defaults to <see cref="AnsiConsole.Console"/>.</param>
        public static void RenderIfAvailable(
            Task<UpdateInfo?> checkTask,
            TimeSpan? waitFor = null,
            IAnsiConsole? console = null)
        {
            ArgumentNullException.ThrowIfNull(checkTask);

            // Wait() on a faulted/cancelled task throws — keep the whole
            // settle-then-read sequence inside the try/catch so background-
            // check failures never derail the main app.
            UpdateInfo? info;
            try
            {
                if (!checkTask.Wait(waitFor ?? DefaultRenderWaitFor))
                {
                    return;
                }
                info = checkTask.GetAwaiter().GetResult();
            }
            catch
            {
                return;
            }
            if (info is null || !info.IsUpdateAvailable) return;

            var ansi = console ?? AnsiConsole.Console;
            if (info.ReleaseUrl is not null)
            {
                ansi.MarkupLineInterpolated(CultureInfo.InvariantCulture,
                    $"[magenta]→[/] New version [bold]{info.LatestTag}[/] available (you're on [bold]{info.CurrentVersion}[/]) — {info.ReleaseUrl}");
            }
            else
            {
                ansi.MarkupLineInterpolated(CultureInfo.InvariantCulture,
                    $"[magenta]→[/] New version [bold]{info.LatestTag}[/] available (you're on [bold]{info.CurrentVersion}[/]).");
            }
        }
    }
}
