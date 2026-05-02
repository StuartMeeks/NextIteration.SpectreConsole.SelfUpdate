using System.Reflection;

namespace NextIteration.SpectreConsole.SelfUpdate.Internal
{
    /// <summary>
    /// The package's own version. Read once at static-init from
    /// <see cref="AssemblyInformationalVersionAttribute"/>, with any
    /// <c>+sha</c> build metadata stripped. Used wherever the package
    /// needs to identify itself to a remote service (e.g. the GitHub REST
    /// API <c>User-Agent</c> header) so the literal version string isn't
    /// duplicated across the codebase.
    /// </summary>
    internal static class PackageVersion
    {
        public static string Current { get; } = Resolve();

        private static string Resolve()
        {
            var asm = typeof(PackageVersion).Assembly;
            var attr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var raw = attr?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "0";
            }
            var plus = raw.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? raw : raw[..plus];
        }
    }
}
