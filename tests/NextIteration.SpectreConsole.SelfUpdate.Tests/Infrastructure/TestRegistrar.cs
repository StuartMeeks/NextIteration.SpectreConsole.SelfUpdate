using Microsoft.Extensions.DependencyInjection;

using Spectre.Console.Cli;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure
{
    /// <summary>
    /// Minimal Spectre.Console.Cli <see cref="ITypeRegistrar"/> backed by a
    /// pre-built MS DI <see cref="IServiceProvider"/>. Tests inject every
    /// dependency the commands need via the <c>configure</c> action; the
    /// <see cref="Register"/> / <see cref="RegisterInstance"/> /
    /// <see cref="RegisterLazy"/> methods are no-ops so Spectre's own
    /// default registrations don't override our pre-wired stubs (matches
    /// the pattern in the demo project's <c>ServiceProviderTypeRegistrar</c>).
    /// </summary>
    internal sealed class TestRegistrar : ITypeRegistrar
    {
        private readonly IServiceProvider _provider;

        public TestRegistrar(Action<IServiceCollection> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);
            var services = new ServiceCollection();
            configure(services);
            _provider = services.BuildServiceProvider();
        }

        public ITypeResolver Build() => new TestResolver(_provider);

        public void Register(Type service, Type implementation) { }

        public void RegisterInstance(Type service, object implementation) { }

        public void RegisterLazy(Type service, Func<object> factory) { }

        private sealed class TestResolver : ITypeResolver
        {
            private readonly IServiceProvider _provider;

            public TestResolver(IServiceProvider provider) => _provider = provider;

            public object? Resolve(Type? type) => type is null ? null : _provider.GetService(type);
        }
    }
}
