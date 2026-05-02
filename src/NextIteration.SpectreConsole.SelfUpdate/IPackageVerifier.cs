namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// Validates a downloaded asset before the installer extracts it.
    /// Multiple verifiers can be registered — the installer runs every
    /// registered <see cref="IPackageVerifier"/> in DI registration order
    /// and refuses to extract unless all of them succeed. The default
    /// stack ships with a SHA-256 verifier (toggle via
    /// <see cref="SelfUpdaterOptions.UseDefaultSha256Verifier"/>); add
    /// extra verifiers (minisign, cosign, Authenticode, GPG) by calling
    /// <see cref="SelfUpdaterOptions.AddVerifier{TVerifier}"/> or
    /// <c>AddVerifier</c> with a factory.
    /// </summary>
    public interface IPackageVerifier
    {
        /// <summary>
        /// Verify the file at <paramref name="downloadedFilePath"/>. Throw
        /// <see cref="UpdateException"/> on failure with a human-readable
        /// message; return normally on success.
        /// </summary>
        /// <param name="downloadedFilePath">Absolute path to the downloaded archive.</param>
        /// <param name="release">The release the asset belongs to.</param>
        /// <param name="asset">The specific asset that was downloaded.</param>
        /// <param name="ct">Cancellation token.</param>
        Task VerifyAsync(
            string downloadedFilePath,
            RemoteRelease release,
            ReleaseAsset asset,
            CancellationToken ct);
    }
}
