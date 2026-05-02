using Microsoft.Extensions.DependencyInjection;

using NextIteration.SpectreConsole.SelfUpdate.Commands;
using NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure;

using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Testing;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Commands
{
    public sealed class UpdateCommandTests
    {
        private delegate Task<int> Runner(params string[] args);

        private static readonly UpdateInfo UpdateAvailable =
            new(CurrentVersion: "1.0.0", LatestTag: "v1.4.2", IsUpdateAvailable: true, ReleaseUrl: new Uri("https://example.com/r/v1.4.2"));

        private static readonly UpdateInfo UpToDate =
            new(CurrentVersion: "1.4.2", LatestTag: "v1.4.2", IsUpdateAvailable: false, ReleaseUrl: null);

        [Fact]
        public async Task Execute_when_already_up_to_date_returns_zero()
        {
            var (run, console, _) = BuildHarness(checker => checker.CheckImpl = _ => Task.FromResult<UpdateInfo?>(UpToDate));

            var exit = await run("--yes");

            Assert.Equal(0, exit);
            Assert.Contains("Already up to date", console.Output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Execute_when_check_returns_null_returns_one()
        {
            var (run, console, _) = BuildHarness(checker => checker.CheckImpl = _ => Task.FromResult<UpdateInfo?>(null));

            var exit = await run("--yes");

            Assert.Equal(1, exit);
            Assert.Contains("Could not determine the latest release", console.Output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Execute_when_check_throws_returns_one()
        {
            var (run, console, _) = BuildHarness(checker => checker.CheckImpl = _ => throw new InvalidOperationException("network down"));

            var exit = await run("--yes");

            Assert.Equal(1, exit);
            Assert.Contains("Could not determine the latest release", console.Output, StringComparison.Ordinal);
            Assert.Contains("network down", console.Output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Execute_when_user_declines_prompt_returns_two()
        {
            var (run, console, _) = BuildHarness(
                configChecker: checker => checker.CheckImpl = _ => Task.FromResult<UpdateInfo?>(UpdateAvailable));

            console.Profile.Capabilities.Interactive = true;
            console.Input.PushTextWithEnter("n");

            var exit = await run();   // no --yes

            Assert.Equal(2, exit);
            Assert.Contains("Aborted", console.Output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Execute_when_install_succeeds_returns_zero_and_calls_install()
        {
            var installCalls = 0;
            var (run, console, _) = BuildHarness(
                configChecker: checker => checker.CheckImpl = _ => Task.FromResult<UpdateInfo?>(UpdateAvailable),
                configUpdater: updater => updater.InstallImpl = (_, _) =>
                {
                    installCalls++;
                    return Task.CompletedTask;
                });

            var exit = await run("--yes");

            Assert.Equal(0, exit);
            Assert.Equal(1, installCalls);
            Assert.Contains("Installed", console.Output, StringComparison.Ordinal);
            Assert.Contains("v1.4.2", console.Output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Execute_when_install_throws_UpdateException_returns_three()
        {
            var (run, console, _) = BuildHarness(
                configChecker: checker => checker.CheckImpl = _ => Task.FromResult<UpdateInfo?>(UpdateAvailable),
                configUpdater: updater => updater.InstallImpl = (_, _) => throw new UpdateException("boom"));

            var exit = await run("--yes");

            Assert.Equal(3, exit);
            Assert.Contains("Update failed", console.Output, StringComparison.Ordinal);
            Assert.Contains("boom", console.Output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Execute_when_force_and_up_to_date_proceeds_to_install()
        {
            var installCalls = 0;
            var (run, _, _) = BuildHarness(
                configChecker: checker => checker.CheckImpl = _ => Task.FromResult<UpdateInfo?>(UpToDate),
                configUpdater: updater => updater.InstallImpl = (_, _) =>
                {
                    installCalls++;
                    return Task.CompletedTask;
                });

            var exit = await run("--yes", "--force");

            Assert.Equal(0, exit);
            Assert.Equal(1, installCalls);
        }

        [Fact]
        public async Task Execute_prints_current_and_latest_versions()
        {
            var (run, console, _) = BuildHarness(checker =>
            {
                checker.CurrentVersion = "1.0.0";
                checker.CheckImpl = _ => Task.FromResult<UpdateInfo?>(UpdateAvailable);
            });

            await run("--yes");

            Assert.Contains("Current version", console.Output, StringComparison.Ordinal);
            Assert.Contains("1.0.0", console.Output, StringComparison.Ordinal);
            Assert.Contains("Latest release", console.Output, StringComparison.Ordinal);
            Assert.Contains("v1.4.2", console.Output, StringComparison.Ordinal);
        }

        // ---------- helpers ----------

        private static (Runner Run, TestConsole Console, StubSelfUpdater Updater) BuildHarness(
            Action<StubUpdateChecker>? configChecker = null,
            Action<StubSelfUpdater>? configUpdater = null,
            Action<StubUpdateInstaller>? configInstaller = null)
        {
            var checker = new StubUpdateChecker();
            configChecker?.Invoke(checker);
            var updater = new StubSelfUpdater();
            configUpdater?.Invoke(updater);
            var installer = new StubUpdateInstaller();
            configInstaller?.Invoke(installer);
            var console = new TestConsole();

            var registrar = new TestRegistrar(s =>
            {
                s.AddSingleton<ISelfUpdater>(updater);
                s.AddSingleton<IUpdateChecker>(checker);
                s.AddSingleton<IUpdateInstaller>(installer);
                s.AddSingleton<IAnsiConsole>(console);
                s.AddSingleton<UpdateCommand>();
            });

            var app = new CommandApp<UpdateCommand>(registrar);
            app.Configure(c => c.PropagateExceptions());

            Runner run = args => app.RunAsync(args);
            return (run, console, updater);
        }
    }
}
