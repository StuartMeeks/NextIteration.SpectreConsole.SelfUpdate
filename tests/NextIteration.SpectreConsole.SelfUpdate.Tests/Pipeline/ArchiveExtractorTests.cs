using System.Formats.Tar;
using System.IO.Compression;

using NextIteration.SpectreConsole.SelfUpdate.Pipeline;
using NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Pipeline
{
    public sealed class ArchiveExtractorTests
    {
        [Fact]
        public async Task ExtractAsync_extracts_zip_archive()
        {
            using var dir = new TempDir();
            var zipPath = dir.Combine("archive.zip");
            CreateZip(zipPath, ("file1.txt", "hello"), ("nested/file2.txt", "world"));
            var dest = dir.Combine("out");

            await ArchiveExtractor.ExtractAsync(zipPath, dest, CancellationToken.None);

            Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(dest, "file1.txt")));
            Assert.Equal("world", await File.ReadAllTextAsync(Path.Combine(dest, "nested", "file2.txt")));
        }

        [Fact]
        public async Task ExtractAsync_extracts_tar_gz_archive()
        {
            using var dir = new TempDir();
            var tarGzPath = dir.Combine("archive.tar.gz");
            CreateTarGz(tarGzPath, ("file1.txt", "hello"), ("nested/file2.txt", "world"));
            var dest = dir.Combine("out");

            await ArchiveExtractor.ExtractAsync(tarGzPath, dest, CancellationToken.None);

            Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(dest, "file1.txt")));
            Assert.Equal("world", await File.ReadAllTextAsync(Path.Combine(dest, "nested", "file2.txt")));
        }

        [Fact]
        public async Task ExtractAsync_throws_for_unsupported_extension()
        {
            using var dir = new TempDir();
            var rarPath = dir.Combine("archive.rar");
            await File.WriteAllBytesAsync(rarPath, new byte[] { 0x52, 0x61, 0x72, 0x21 });

            var ex = await Assert.ThrowsAsync<UpdateException>(() =>
                ArchiveExtractor.ExtractAsync(rarPath, dir.Combine("out"), CancellationToken.None));

            Assert.Contains("Unsupported archive format", ex.Message, StringComparison.Ordinal);
        }

        private static void CreateZip(string path, params (string Name, string Content)[] entries)
        {
            using var fs = File.Create(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        private static void CreateTarGz(string path, params (string Name, string Content)[] entries)
        {
            using var staging = new TempDir("tar-stage");
            foreach (var (name, content) in entries)
            {
                var full = Path.Combine(staging.Path, name);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content);
            }

            using var fs = File.Create(path);
            using var gz = new GZipStream(fs, CompressionLevel.Optimal);
            TarFile.CreateFromDirectory(staging.Path, gz, includeBaseDirectory: false);
        }
    }
}
