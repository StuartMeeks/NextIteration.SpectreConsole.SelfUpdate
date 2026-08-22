using Microsoft.Extensions.DependencyInjection;

using NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure;

using Spectre.Console;
using Spectre.Console.Testing;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests
{
    public sealed class UpdateCleanupTests
    {
        [Fact]
        public void Run_when_no_pending_cleanup_writes_nothing_and_cleans()
        {
            var installer = new StubUpdateInstaller { HasPendingCleanup = false };
            using var console = new TestConsole();

            UpdateCleanup.Run(installer, console);

            Assert.Equal(1, installer.CleanupCallCount);
            Assert.Equal(string.Empty, console.Output);
        }

        [Fact]
        public void Run_when_pending_cleanup_renders_message_and_cleans()
        {
            var installer = new StubUpdateInstaller { HasPendingCleanup = true };
            using var console = new TestConsole();

            UpdateCleanup.Run(installer, console);

            Assert.Equal(1, installer.CleanupCallCount);
            Assert.Contains("Cleaning up previous update", console.Output, StringComparison.Ordinal);
        }

        [Fact]
        public void Run_via_service_provider_resolves_installer_and_console()
        {
            var installer = new StubUpdateInstaller { HasPendingCleanup = true };
            using var console = new TestConsole();
            var services = new ServiceCollection();
            services.AddSingleton<IUpdateInstaller>(installer);
            services.AddSingleton<IAnsiConsole>(console);
            using var sp = services.BuildServiceProvider();

            UpdateCleanup.Run(sp);

            Assert.Equal(1, installer.CleanupCallCount);
            Assert.Contains("Cleaning up previous update", console.Output, StringComparison.Ordinal);
        }

        // Run is overloaded on IServiceProvider and IUpdateInstaller. A typed
        // local pins which overload each test targets without an upcast
        // expression at the call site.
        [Fact]
        public void Run_with_null_services_throws()
        {
            IServiceProvider services = null!;

            Assert.Throws<ArgumentNullException>(() => UpdateCleanup.Run(services));
        }

        [Fact]
        public void Run_with_null_installer_throws()
        {
            IUpdateInstaller installer = null!;
            using var console = new TestConsole();

            Assert.Throws<ArgumentNullException>(() => UpdateCleanup.Run(installer, console));
        }

        [Fact]
        public void Run_with_null_console_throws() => Assert.Throws<ArgumentNullException>(() => UpdateCleanup.Run(new StubUpdateInstaller(), null!));
    }
}
