namespace NextIteration.SpectreConsole.SelfUpdate.Internal
{
    /// <summary>
    /// Buffered stream-to-stream copy with progress reporting. Shared by
    /// every built-in <see cref="IUpdateSource"/> so download progress is
    /// reported consistently across HTTP and gh-CLI transports.
    /// </summary>
    internal static class StreamCopy
    {
        private const int BufferSize = 81920;

        public static async Task CopyWithProgressAsync(
            Stream source,
            Stream destination,
            long? total,
            IProgress<DownloadProgress>? progress,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(destination);

            var buffer = new byte[BufferSize];
            long copied = 0;
            int read;
            progress?.Report(new DownloadProgress(0, total));
            while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                copied += read;
                progress?.Report(new DownloadProgress(copied, total));
            }
        }
    }
}
