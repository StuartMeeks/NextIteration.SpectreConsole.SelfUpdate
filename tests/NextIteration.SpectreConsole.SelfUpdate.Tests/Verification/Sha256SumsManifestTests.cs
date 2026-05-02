using NextIteration.SpectreConsole.SelfUpdate.Verification;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Verification
{
    public sealed class Sha256SumsManifestTests
    {
        [Fact]
        public void Parse_simple_two_field_lines()
        {
            const string content =
                "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890  myapp-linux-x64.tar.gz\n" +
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  myapp-osx-arm64.tar.gz\n";

            var lookup = Sha256SumsManifest.Parse(content);

            Assert.Equal(2, lookup.Count);
            Assert.Equal("abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890", lookup["myapp-linux-x64.tar.gz"]);
            Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", lookup["myapp-osx-arm64.tar.gz"]);
        }

        [Fact]
        public void Parse_strips_leading_asterisk_binary_marker()
        {
            const string content = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890 *foo.tar.gz\n";

            var lookup = Sha256SumsManifest.Parse(content);

            Assert.Single(lookup);
            Assert.True(lookup.ContainsKey("foo.tar.gz"));
        }

        [Fact]
        public void Parse_ignores_comments_and_blank_lines()
        {
            const string content =
                "# this is a comment\n" +
                "\n" +
                "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890  foo.zip\n" +
                "    \n";

            var lookup = Sha256SumsManifest.Parse(content);

            Assert.Single(lookup);
            Assert.True(lookup.ContainsKey("foo.zip"));
        }

        [Fact]
        public void Parse_drops_lines_with_invalid_hash_length()
        {
            const string content =
                "shorthex foo.zip\n" +
                "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890  good.zip\n";

            var lookup = Sha256SumsManifest.Parse(content);

            Assert.Single(lookup);
            Assert.True(lookup.ContainsKey("good.zip"));
        }

        [Fact]
        public void Parse_drops_lines_with_non_hex_characters()
        {
            const string content =
                "ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ  bad.zip\n" +
                "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890  good.zip\n";

            var lookup = Sha256SumsManifest.Parse(content);

            Assert.Single(lookup);
            Assert.True(lookup.ContainsKey("good.zip"));
        }

        [Fact]
        public void Parse_lowercases_hex_for_consistent_lookup()
        {
            const string content =
                "ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890  foo.zip\n";

            var lookup = Sha256SumsManifest.Parse(content);

            Assert.Equal("abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890", lookup["foo.zip"]);
        }

        [Fact]
        public void Parse_lookup_is_case_insensitive_on_filename()
        {
            const string content =
                "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890  Foo.Zip\n";

            var lookup = Sha256SumsManifest.Parse(content);

            Assert.True(lookup.ContainsKey("foo.zip"));
            Assert.True(lookup.ContainsKey("FOO.ZIP"));
        }

        [Fact]
        public void Parse_supports_tab_separated_lines()
        {
            const string content =
                "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890\tfoo.zip\n";

            var lookup = Sha256SumsManifest.Parse(content);

            Assert.Single(lookup);
        }

        [Fact]
        public void Parse_throws_on_null_input()
        {
            Assert.Throws<ArgumentNullException>(() => Sha256SumsManifest.Parse(null!));
        }
    }
}
