using Microsoft.Extensions.DependencyInjection;

using NextIteration.SpectreConsole.SelfUpdate;

using Spectre.Console;
using Spectre.Console.Cli;

internal sealed class Program
{
    public static int Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);

        services.AddSelfUpdater(opts =>
        {
            opts.AppName = "selfupdate-demo";
            // Replace with your repo. The HttpGitHubReleaseSource works for
            // public repos out of the box; private repos can switch to
            // GitHubTransport.GhCli or supply a token.
            opts.UseGitHubReleases("StuartMeeks/NextIteration.SpectreConsole.SelfUpdate");
        });

        using var serviceProvider = services.BuildServiceProvider();

        // 1. Sweep up the previous install (the running new binary is proof
        //    the last swap completed). Idempotent — safe to call every run.
        serviceProvider.GetRequiredService<IUpdateInstaller>().CleanupOldInstall();

        // 2. Kick off the background "is there a new version?" probe. It's
        //    short-timeout and read-through-cached; on the warm path it
        //    resolves in microseconds.
        var checkTask = UpdateBanner.KickOffCheck(serviceProvider);

        // 3. Wire and run the CLI. The Spectre commands are already in DI —
        //    AddSelfUpdater registers UpdateCommand and UpdateCheckCommand
        //    so the TypeResolver can return them directly.
        var app = new CommandApp(new ServiceProviderTypeRegistrar(serviceProvider));
        app.Configure(config =>
        {
            config.SetApplicationName("selfupdate-demo");
            config.AddUpdateCommand();   // exposes `selfupdate-demo update`
        });
        var exitCode = app.Run(args);

        // 4. Render the banner if the probe came back positive. No-op when
        //    the network was slow or the user is already on the latest tag.
        UpdateBanner.RenderIfAvailable(checkTask);
        return exitCode;
    }
}

/// <summary>
/// Trivial Spectre.Console.Cli registrar that delegates to a pre-built
/// MS DI <see cref="IServiceProvider"/>. Because every command is already
/// registered in DI by <c>AddSelfUpdater</c>, the Register* methods are
/// no-ops here.
/// </summary>
internal sealed class ServiceProviderTypeRegistrar : ITypeRegistrar
{
    private readonly IServiceProvider _serviceProvider;

    public ServiceProviderTypeRegistrar(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ITypeResolver Build() => new ServiceProviderTypeResolver(_serviceProvider);

    public void Register(Type service, Type implementation) { }
    public void RegisterInstance(Type service, object implementation) { }
    public void RegisterLazy(Type service, Func<object> factory) { }
}

internal sealed class ServiceProviderTypeResolver : ITypeResolver
{
    private readonly IServiceProvider _serviceProvider;

    public ServiceProviderTypeResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object? Resolve(Type? type) => type is null ? null : _serviceProvider.GetService(type);
}
