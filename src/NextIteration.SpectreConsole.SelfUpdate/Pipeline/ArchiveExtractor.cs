using System.Formats.Tar;
using System.IO.Compression;

namespace NextIteration.SpectreConsole.SelfUpdate.Pipeline
{
    /// <summary>
    /// Expands a downloaded archive into a destination directory.
    /// Recognises <c>.zip</c>, <c>.tar.gz</c>, and <c>.tgz</c> by file
    /// extension. Throws <see cref="UpdateException"/> for unknown
    /// extensions or extraction failures so the installer can surface a
    /// human-readable message.
    /// </summary>
    internal static class ArchiveExtractor
    {
        public static async Task ExtractAsync(string archivePath, string destinationDirectory, CancellationToken ct)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

            Directory.CreateDirectory(destinationDirectory);

            try
            {
                if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    await ExtractZipAsync(archivePath, destinationDirectory, ct).ConfigureAwait(false);
                    return;
                }
                if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
                    || archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
                {
                    await ExtractTarGzAsync(archivePath, destinationDirectory, ct).ConfigureAwait(false);
                    return;
                }
                throw new UpdateException(
                    $"Unsupported archive format for '{Path.GetFileName(archivePath)}'. Supported: .zip, .tar.gz, .tgz.");
            }
            catch (UpdateException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new UpdateException($"Failed to extract '{Path.GetFileName(archivePath)}': {ex.Message}", ex);
            }
        }

        private static Task ExtractZipAsync(string archivePath, string destinationDirectory, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ZipFile.ExtractToDirectory(archivePath, destinationDirectory, overwriteFiles: true);
            return Task.CompletedTask;
        }

        private static async Task ExtractTarGzAsync(string archivePath, string destinationDirectory, CancellationToken ct)
        {
            await using var fileStream = File.OpenRead(archivePath);
            await using var gzip = new GZipStream(fileStream, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gzip, destinationDirectory, overwriteFiles: true, ct).ConfigureAwait(false);
        }
    }
}
