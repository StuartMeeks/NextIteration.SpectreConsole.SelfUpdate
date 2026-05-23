using Microsoft.Extensions.DependencyInjection;

using NextIteration.SpectreConsole.SelfUpdate.Commands;
using NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure;

using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Testing;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Commands
{
    public sealed class UpdateCheckCommandTests
    {
        private delegate Task<int> Runner(params string[] args);

        [Fact]
        public async Task Execute_with_prerelease_flag_passes_true_override_to_checker()
        {
            var (run, _, checker) = BuildHarness(c =>
            {
                c.CurrentVersion = "1.0.0";
                c.CheckImpl = _ => Task.FromResult<UpdateInfo?>(
                    new UpdateInfo("1.0.0", "v1.4.2", IsUpdateAvailable: false, ReleaseUrl: null));
            });

            await run("--prerelease");

            Assert.True(checker.LastIncludePrereleasesOverride);
        }

        [Fact]
        public async Task Execute_without_prerelease_flag_passes_null_override_to_checker()
        {
            var (run, _, checker) = BuildHarness(c =>
            {
                c.CurrentVersion = "1.0.0";
                c.CheckImpl = _ => Task.FromResult<UpdateInfo?>(
                    new UpdateInfo("1.0.0", "v1.4.2", IsUpdateAvailable: false, ReleaseUrl: null));
            });

            await run();

            Assert.Null(checker.LastIncludePrereleasesOverride);
        }

        [Fact]
        public async Task Execute_when_up_to_date_returns_zero()
        {
            var (run, console, _) = BuildHarness(checker =>
            {
                checker.CurrentVersion = "1.4.2";
                checker.CheckImpl = _ => Task.FromResult<UpdateInfo?>(
                    new UpdateInfo("1.4.2", "v1.4.2", IsUpdateAvailable: false, ReleaseUrl: null));
            });

            var exit = await run();

            Assert.Equal(0, exit);
            Assert.Contains("Already up to date", console.Output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Execute_when_update_available_returns_two()
        {
            var (run, console, _) = BuildHarness(checker =>
            {
                checker.CurrentVersion = "1.0.0";
                checker.CheckImpl = _ => Task.FromResult<UpdateInfo?>(
                    new UpdateInfo("1.0.0", "v1.4.2", IsUpdateAvailable: true,
                        ReleaseUrl: new Uri("https://example.com/r/v1.4.2")));
            });

            var exit = await run();

            Assert.Equal(2, exit);
            Assert.Contains("Update available", console.Output, StringComparison.Ordinal);
            Assert.Contains("v1.4.2", console.Output, StringComparison.Ordinal);
            Assert.Contains("Release notes", console.Output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Execute_when_check_returns_null_returns_one()
        {
            var (run, console, _) = BuildHarness(checker =>
                checker.CheckImpl = _ => Task.FromResult<UpdateInfo?>(null));

            var exit = await run();

            Assert.Equal(1, exit);
            Assert.Contains("Could not determine the latest release", console.Output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Execute_when_check_throws_returns_one()
        {
            var (run, console, _) = BuildHarness(checker =>
                checker.CheckImpl = _ => throw new InvalidOperationException("offline"));

            var exit = await run();

            Assert.Equal(1, exit);
            Assert.Contains("Could not determine the latest release", console.Output, StringComparison.Ordinal);
            Assert.Contains("offline", console.Output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Execute_when_no_release_url_omits_release_notes_line()
        {
            var (run, console, _) = BuildHarness(checker =>
            {
                checker.CurrentVersion = "1.0.0";
                checker.CheckImpl = _ => Task.FromResult<UpdateInfo?>(
                    new UpdateInfo("1.0.0", "v1.4.2", IsUpdateAvailable: true, ReleaseUrl: null));
            });

            var exit = await run();

            Assert.Equal(2, exit);
            Assert.DoesNotContain("Release notes", console.Output, StringComparison.Ordinal);
        }

        // ---------- helpers ----------

        private static (Runner Run, TestConsole Console, StubUpdateChecker Checker) BuildHarness(Action<StubUpdateChecker> configChecker)
        {
            var checker = new StubUpdateChecker();
            configChecker(checker);
            var console = new TestConsole();

            var registrar = new TestRegistrar(s =>
            {
                s.AddSingleton<IUpdateChecker>(checker);
                s.AddSingleton<IAnsiConsole>(console);
                s.AddSingleton<UpdateCheckCommand>();
            });

            var app = new CommandApp<UpdateCheckCommand>(registrar);
            app.Configure(c => c.PropagateExceptions());

            Runner run = args => app.RunAsync(args);
            return (run, console, checker);
        }
    }
}
