namespace NextIteration.SpectreConsole.SelfUpdate.Verification
{
    /// <summary>
    /// Parser for the de-facto-standard <c>SHA256SUMS.txt</c> format: each
    /// non-comment line is <c>&lt;hex&gt; [*]&lt;filename&gt;</c>, separated by
    /// one or more spaces or tabs. <c>#</c>-prefixed lines and blank lines
    /// are ignored. The leading <c>*</c> on the filename (binary-mode marker
    /// from GNU coreutils) is stripped.
    /// </summary>
    internal static class Sha256SumsManifest
    {
        private static readonly char[] FieldSeparators = { ' ', '\t' };

        public static IReadOnlyDictionary<string, string> Parse(string content)
        {
            ArgumentNullException.ThrowIfNull(content);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                var split = line.IndexOfAny(FieldSeparators);
                if (split <= 0) continue;

                var hex = line[..split].Trim();
                var name = line[(split + 1)..].Trim().TrimStart('*');

                if (hex.Length != 64 || name.Length == 0) continue;
                if (!IsHex(hex)) continue;

                result[name] = hex.ToLowerInvariant();
            }
            return result;
        }

        private static bool IsHex(string s)
        {
            foreach (var c in s)
            {
                var ok = c is >= '0' and <= '9' || c is >= 'a' and <= 'f' || c is >= 'A' and <= 'F';
                if (!ok) return false;
            }
            return true;
        }
    }
}
