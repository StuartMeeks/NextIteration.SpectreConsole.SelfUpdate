using Microsoft.Extensions.DependencyInjection;

using NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure;

using Spectre.Console;
using Spectre.Console.Testing;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests
{
    public sealed class UpdateBannerTests
    {
        [Fact]
        public async Task KickOffCheck_resolves_self_updater_and_returns_in_flight_task()
        {
            var probe = new UpdateInfo("1.0.0", "v1.4.2", IsUpdateAvailable: true, ReleaseUrl: null);
            var updater = new StubSelfUpdater { CheckImpl = _ => Task.FromResult<UpdateInfo?>(probe) };
            using var services = BuildServiceProvider(updater);

            var task = UpdateBanner.KickOffCheck(services);

            Assert.NotNull(task);
            var result = await task;
            Assert.Same(probe, result);
        }

        [Fact]
        public void RenderIfAvailable_when_update_present_renders_banner()
        {
            var info = new UpdateInfo("1.0.0", "v1.4.2", IsUpdateAvailable: true, ReleaseUrl: new Uri("https://example.com/r"));
            var task = Task.FromResult<UpdateInfo?>(info);
            var console = new TestConsole();

            UpdateBanner.RenderIfAvailable(task, waitFor: TimeSpan.FromSeconds(1), console: console);

            Assert.Contains("New version", console.Output, StringComparison.Ordinal);
            Assert.Contains("v1.4.2", console.Output, StringComparison.Ordinal);
            Assert.Contains("1.0.0", console.Output, StringComparison.Ordinal);
            Assert.Contains("https://example.com/r", console.Output, StringComparison.Ordinal);
        }

        [Fact]
        public void RenderIfAvailable_when_no_release_url_omits_link()
        {
            var info = new UpdateInfo("1.0.0", "v1.4.2", IsUpdateAvailable: true, ReleaseUrl: null);
            var task = Task.FromResult<UpdateInfo?>(info);
            var console = new TestConsole();

            UpdateBanner.RenderIfAvailable(task, waitFor: TimeSpan.FromSeconds(1), console: console);

            Assert.Contains("New version", console.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("https://", console.Output, StringComparison.Ordinal);
        }

        [Fact]
        public void RenderIfAvailable_when_info_is_null_writes_nothing()
        {
            var task = Task.FromResult<UpdateInfo?>(null);
            var console = new TestConsole();

            UpdateBanner.RenderIfAvailable(task, waitFor: TimeSpan.FromSeconds(1), console: console);

            Assert.Equal(string.Empty, console.Output);
        }

        [Fact]
        public void RenderIfAvailable_when_up_to_date_writes_nothing()
        {
            var info = new UpdateInfo("1.4.2", "v1.4.2", IsUpdateAvailable: false, ReleaseUrl: null);
            var task = Task.FromResult<UpdateInfo?>(info);
            var console = new TestConsole();

            UpdateBanner.RenderIfAvailable(task, waitFor: TimeSpan.FromSeconds(1), console: console);

            Assert.Equal(string.Empty, console.Output);
        }

        [Fact]
        public void RenderIfAvailable_when_task_does_not_complete_in_time_writes_nothing()
        {
            var tcs = new TaskCompletionSource<UpdateInfo?>();
            var console = new TestConsole();

            UpdateBanner.RenderIfAvailable(tcs.Task, waitFor: TimeSpan.FromMilliseconds(50), console: console);

            Assert.Equal(string.Empty, console.Output);

            // Now complete it post-hoc — the banner already returned, so still nothing.
            tcs.SetResult(new UpdateInfo("1.0.0", "v1.4.2", IsUpdateAvailable: true, ReleaseUrl: null));
            Assert.Equal(string.Empty, console.Output);
        }

        [Fact]
        public void RenderIfAvailable_when_task_faulted_swallows_and_writes_nothing()
        {
            var task = Task.FromException<UpdateInfo?>(new InvalidOperationException("boom"));
            var console = new TestConsole();

            UpdateBanner.RenderIfAvailable(task, waitFor: TimeSpan.FromSeconds(1), console: console);

            Assert.Equal(string.Empty, console.Output);
        }

        [Fact]
        public void RenderIfAvailable_with_null_task_throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                UpdateBanner.RenderIfAvailable(null!, waitFor: null, console: null));
        }

        private static ServiceProvider BuildServiceProvider(StubSelfUpdater updater)
        {
            var services = new ServiceCollection();
            services.AddSingleton<ISelfUpdater>(updater);
            return services.BuildServiceProvider();
        }
    }
}
