using System.Runtime.InteropServices;

namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// Detects the .NET runtime identifier (RID) of the currently running
    /// process. Used by the default <see cref="IAssetResolver"/> to pick the
    /// matching release archive.
    /// </summary>
    /// <remarks>
    /// Returns the short RID form expected in release-asset filenames:
    /// <c>win-x64</c>, <c>win-arm64</c>, <c>linux-x64</c>,
    /// <c>linux-arm64</c>, <c>osx-x64</c>, <c>osx-arm64</c>. The package
    /// does not currently distinguish glibc vs. musl Linux variants — most
    /// CLI release pipelines publish a single <c>linux-x64</c> /
    /// <c>linux-arm64</c> binary that targets glibc, and Alpine users opt
    /// in via a custom <see cref="IAssetResolver"/>.
    /// </remarks>
    public static class RuntimeIdentifier
    {
        /// <summary>
        /// Resolve the running RID. Throws <see cref="PlatformNotSupportedException"/>
        /// on platform/architecture combinations the package does not
        /// recognise — consumers running on exotic hardware can override
        /// detection by registering a custom <see cref="IAssetResolver"/>.
        /// </summary>
        public static string Detect()
        {
            var os = OsToken();
            var arch = ArchToken();
            return $"{os}-{arch}";
        }

        private static string OsToken()
        {
            if (OperatingSystem.IsWindows()) return "win";
            if (OperatingSystem.IsLinux()) return "linux";
            if (OperatingSystem.IsMacOS()) return "osx";
            throw new PlatformNotSupportedException(
                $"Unsupported operating system: {RuntimeInformation.OSDescription}");
        }

        private static string ArchToken() => RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => throw new PlatformNotSupportedException(
                $"Unsupported architecture: {RuntimeInformation.OSArchitecture}"),
        };
    }
}
