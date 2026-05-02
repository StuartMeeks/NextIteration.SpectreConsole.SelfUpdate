namespace NextIteration.SpectreConsole.SelfUpdate.Resolution
{
    /// <summary>
    /// Adapter that surfaces a delegate-style resolver as an
    /// <see cref="IAssetResolver"/>. Used by
    /// <see cref="SelfUpdaterOptions.UseAssetResolver(Func{RemoteRelease, string, ReleaseAsset?})"/>.
    /// </summary>
    internal sealed class FuncAssetResolver : IAssetResolver
    {
        private readonly Func<RemoteRelease, string, ReleaseAsset?> _func;

        public FuncAssetResolver(Func<RemoteRelease, string, ReleaseAsset?> func)
        {
            ArgumentNullException.ThrowIfNull(func);
            _func = func;
        }

        public ReleaseAsset? Resolve(RemoteRelease release, string runtimeIdentifier) =>
            _func(release, runtimeIdentifier);
    }
}
