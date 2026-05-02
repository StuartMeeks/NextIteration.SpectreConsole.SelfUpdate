using NextIteration.SpectreConsole.SelfUpdate.Pipeline;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Pipeline
{
    public sealed class UpdateCheckerVersionTests
    {
        [Theory]
        [InlineData("1.0.0", "1.0.1", true)]
        [InlineData("1.0.0", "2.0.0", true)]
        [InlineData("1.0.0", "v1.0.1", true)]
        [InlineData("v1.0.0", "1.0.1", true)]
        [InlineData("1.0.0", "1.0.0", false)]
        [InlineData("1.0.1", "1.0.0", false)]
        [InlineData("2.0.0", "1.99.99", false)]
        public void IsNewer_when_numeric_versions_compares_correctly(string current, string latest, bool expected)
        {
            Assert.Equal(expected, UpdateChecker.IsNewer(current, latest));
        }

        [Theory]
        [InlineData("1.0.0-beta.1", "1.0.0", true)]   // current has prerelease, latest doesn't → upgrade
        [InlineData("1.0.0", "1.0.0-beta.1", false)]  // current is stable, latest is prerelease at same numeric → not upgrade
        [InlineData("1.0.0-alpha", "1.0.0-beta", true)]  // alpha < beta lexicographically
        [InlineData("1.0.0-beta", "1.0.0-alpha", false)] // beta > alpha lexicographically
        [InlineData("1.0.0-beta.1", "1.0.0-beta.2", true)]
        [InlineData("1.0.0-beta", "1.0.0-beta", false)]
        public void IsNewer_with_semver_prerelease_compares_correctly(string current, string latest, bool expected)
        {
            Assert.Equal(expected, UpdateChecker.IsNewer(current, latest));
        }

        [Theory]
        [InlineData("not-a-version", "1.0.0", false)]
        [InlineData("1.0.0", "not-a-version", false)]
        [InlineData("", "1.0.0", false)]
        public void IsNewer_with_unparseable_versions_returns_false(string current, string latest, bool expected)
        {
            Assert.Equal(expected, UpdateChecker.IsNewer(current, latest));
        }

        [Theory]
        [InlineData("1.0.0+abc123", "1.0.0")]
        [InlineData("1.2.3+sha.deadbeef", "1.2.3")]
        [InlineData("1.0.0", "1.0.0")]
        public void StripBuildMetadata_removes_plus_suffix(string input, string expected)
        {
            Assert.Equal(expected, UpdateChecker.StripBuildMetadata(input));
        }

        [Theory]
        [InlineData("v1.0.0", "1.0.0")]
        [InlineData("V2.3.4", "2.3.4")]
        [InlineData("1.0.0", "1.0.0")]
        [InlineData("", "")]
        public void StripLeadingV_removes_v_prefix(string input, string expected)
        {
            Assert.Equal(expected, UpdateChecker.StripLeadingV(input));
        }

        [Theory]
        [InlineData("my-app", "MY_APP_SKIP_UPDATE_CHECK")]
        [InlineData("plapp", "PLAPP_SKIP_UPDATE_CHECK")]
        [InlineData("My App 2", "MY_APP_2_SKIP_UPDATE_CHECK")]
        [InlineData("", "")]
        public void ComputeDefaultSkipEnvVarName_sanitizes_app_name(string appName, string expected)
        {
            Assert.Equal(expected, UpdateChecker.ComputeDefaultSkipEnvVarName(appName));
        }
    }
}
