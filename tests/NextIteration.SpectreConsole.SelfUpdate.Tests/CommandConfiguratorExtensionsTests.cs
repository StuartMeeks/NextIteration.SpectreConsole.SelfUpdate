using Microsoft.Extensions.DependencyInjection;

using NextIteration.SpectreConsole.SelfUpdate.Commands;
using NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure;

using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Testing;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests
{
    public sealed class CommandConfiguratorExtensionsTests : IDisposable
    {
        // The harness below hands its TestConsole to the caller, which reads
        // console.Output after the harness method has returned — so the console
        // cannot be disposed at its creation site. The fixture owns the lifetime
        // instead and disposes every console it handed out when xUnit tears the
        // class down.
        private readonly List<TestConsole> _consoles = [];

        public void Dispose()
        {
            foreach (var console in _consoles)
            {
                console.Dispose();
            }
        }

        private static readonly string[] HelpArgs = ["--help"];
        private static readonly string[] UpdateHelpArgs = ["update", "--help"];
        private static readonly string[] OtaHelpArgs = ["ota", "--help"];

        [Fact]
        public async Task AddUpdateCommand_with_default_name_registers_update_command()
        {
            var (app, console) = BuildApp(c => c.AddUpdateCommand());

            var exit = await app.RunAsync(HelpArgs, TestContext.Current.CancellationToken);

            Assert.Equal(0, exit);
            Assert.Contains("update", console.Output, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task AddUpdateCommand_with_custom_name_registers_under_that_name()
        {
            var (app, console) = BuildApp(c => c.AddUpdateCommand("upgrade"));

            var exit = await app.RunAsync(HelpArgs, TestContext.Current.CancellationToken);

            Assert.Equal(0, exit);
            Assert.Contains("upgrade", console.Output, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task AddUpdateBranch_with_default_name_registers_check_and_apply_subcommands()
        {
            var (app, console) = BuildApp(c => c.AddUpdateBranch());

            var exit = await app.RunAsync(UpdateHelpArgs, TestContext.Current.CancellationToken);

            Assert.Equal(0, exit);
            Assert.Contains("check", console.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("apply", console.Output, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task AddUpdateBranch_with_custom_name_honours_alias()
        {
            var (app, console) = BuildApp(c => c.AddUpdateBranch("ota"));

            var exit = await app.RunAsync(OtaHelpArgs, TestContext.Current.CancellationToken);

            Assert.Equal(0, exit);
            Assert.Contains("check", console.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("apply", console.Output, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AddUpdateCommand_rejects_null_configurator()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CommandConfiguratorExtensions.AddUpdateCommand(configurator: null!));
        }

        [Fact]
        public void AddUpdateBranch_rejects_blank_name()
        {
            // Build a real CommandApp's configurator via the public constructor.
            var registrar = new TestRegistrar(s => { });
            var app = new CommandApp(registrar);
            app.Configure(c =>
            {
                Assert.Throws<ArgumentException>(() => c.AddUpdateBranch("   "));
            });
        }

        // ---------- helpers ----------

        private (CommandApp App, TestConsole Console) BuildApp(Action<IConfigurator> configure)
        {
            var console = new TestConsole();
            _consoles.Add(console);
            var registrar = new TestRegistrar(s =>
            {
                s.AddSingleton<IAnsiConsole>(console);
                s.AddSingleton<ISelfUpdater>(new StubSelfUpdater());
                s.AddSingleton<IUpdateChecker>(new StubUpdateChecker());
                s.AddSingleton<IUpdateInstaller>(new StubUpdateInstaller());
                s.AddSingleton<UpdateCommand>();
                s.AddSingleton<UpdateCheckCommand>();
            });

            var app = new CommandApp(registrar);
            app.Configure(c =>
            {
                c.SetApplicationName("test-cli");
                c.ConfigureConsole(console);
                configure(c);
            });
            return (app, console);
        }
    }
}
