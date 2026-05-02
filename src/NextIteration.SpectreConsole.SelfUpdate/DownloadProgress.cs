namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// Streaming download-progress event emitted by
    /// <see cref="IUpdateSource.DownloadAssetAsync"/>.
    /// </summary>
    /// <param name="BytesDownloaded">Bytes received so far.</param>
    /// <param name="TotalBytes">
    /// Total expected bytes if the source knows the size up front;
    /// <see langword="null"/> when the response is chunked or the source
    /// doesn't publish a size.
    /// </param>
    public sealed record DownloadProgress(long BytesDownloaded, long? TotalBytes);
}
