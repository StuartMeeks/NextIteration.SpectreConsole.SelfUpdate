using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests
{
    public sealed class RuntimeIdentifierTests
    {
        private static readonly string[] KnownOsTokens = { "win", "linux", "osx" };
        private static readonly string[] KnownArchTokens = { "x64", "arm64", "x86", "arm" };

        [Fact]
        public void Detect_returns_known_rid_format()
        {
            var rid = RuntimeIdentifier.Detect();

            Assert.True(rid.Contains('-', StringComparison.Ordinal));
            var parts = rid.Split('-');
            Assert.Equal(2, parts.Length);
            Assert.Contains(parts[0], KnownOsTokens);
            Assert.Contains(parts[1], KnownArchTokens);
        }
    }
}
