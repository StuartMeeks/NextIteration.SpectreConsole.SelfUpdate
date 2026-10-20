using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using NextIteration.SpectreConsole.SelfUpdate.Internal;

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
        /// Stop <see cref="RenderIfAvailable"/> rendering anything for the
        /// rest of this process. Call it from your own command when it has
        /// already shown the operator the latest release (or installed it),
        /// so the post-run banner does not repeat it.
        /// </summary>
        /// <remarks>
        /// The built-in <c>update</c> and <c>update check</c> commands
        /// suppress the banner themselves, and a successful
        /// <see cref="ISelfUpdater.InstallAsync(RemoteRelease, IProgress{UpdateProgressEvent}?, Func{UpdateConflict, CancellationToken, Task{UpdateConflictResolution}}?, CancellationToken)"/>
        /// suppresses it for any release not newer than the one installed, so
        /// apps using only those need not call this. The suppression is
        /// process-wide and cannot be undone.
        /// </remarks>
        public static void Suppress() => UpdateSession.MarkHandled();

        /// <summary>
        /// Wait briefly for <paramref name="checkTask"/> to complete and
        /// render a one-line banner if an update is available. No-op if
        /// the task hasn't completed within <paramref name="waitFor"/>, if
        /// it returned <see langword="null"/>, or if no update is available.
        /// Also a no-op after <see cref="Suppress"/>, after the built-in
        /// <c>update</c> or <c>update check</c> command has shown the latest
        /// release, or when this process has already installed a release at
        /// least as new as the one available.
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

            // The update UX already told the operator about the latest
            // release in this run; don't wait on the task just to repeat it.
            if (UpdateSession.IsHandled)
            {
                return;
            }

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
            catch (Exception ex) when (ex is AggregateException or UpdateException
                or HttpRequestException or IOException or OperationCanceledException)
            {
                // The banner is decoration: a check that failed or timed out
                // simply renders nothing. Task.Wait surfaces a fault as an
                // AggregateException, hence its presence here.
                return;
            }
            if (info is null || !info.IsUpdateAvailable)
            {
                return;
            }

            // info.CurrentVersion is the running process's version, which is
            // still the old binary after an install swapped the files.
            if (UpdateSession.InstalledTag is { } installed
                && !Pipeline.UpdateChecker.IsNewer(installed, info.LatestTag))
            {
                return;
            }

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
