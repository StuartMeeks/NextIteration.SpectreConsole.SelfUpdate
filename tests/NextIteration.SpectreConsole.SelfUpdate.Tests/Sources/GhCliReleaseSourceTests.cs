using NextIteration.SpectreConsole.SelfUpdate.Sources;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Sources
{
    public sealed class GhCliReleaseSourceTests
    {
        private const string Repo = "acme/myapp";

        private const string SingleReleaseJson = """
        {
          "tagName": "v1.4.2",
          "name": "1.4.2",
          "url": "https://github.com/acme/myapp/releases/tag/v1.4.2",
          "publishedAt": "2026-04-30T12:00:00Z",
          "isDraft": false,
          "isPrerelease": false,
          "assets": [
            {
              "name": "myapp-v1.4.2-linux-x64.tar.gz",
              "url": "https://github.com/acme/myapp/releases/download/v1.4.2/myapp-v1.4.2-linux-x64.tar.gz",
              "size": 12345,
              "contentType": "application/gzip"
            }
          ]
        }
        """;

        [Fact]
        public async Task GetLatestAsync_when_stable_invokes_release_view_and_parses_release()
        {
            var runner = new RecordingRunner();
            runner.NextStdout = SingleReleaseJson;
            var source = new GhCliReleaseSource(Repo, includePrereleases: false, runner.RunAsync);

            var release = await source.GetLatestAsync(channel: null, CancellationToken.None);

            Assert.NotNull(release);
            Assert.Equal("v1.4.2", release!.Tag);
            Assert.Single(release.Assets);
            Assert.Equal("v1.4.2", release.Assets[0].Metadata[GhCliReleaseSource.TagMetadataKey]);

            // First (and only) call should have been `release view ... --repo acme/myapp`
            Assert.Single(runner.Invocations);
            Assert.Equal("release", runner.Invocations[0][0]);
            Assert.Equal("view", runner.Invocations[0][1]);
            Assert.Contains(Repo, runner.Invocations[0]);
        }

        [Fact]
        public async Task GetLatestAsync_when_prereleases_enabled_lists_then_views_match()
        {
            var listJson = """
            [
              { "tagName": "v2.0.0-beta.1", "publishedAt": "2026-05-01T00:00:00Z", "isDraft": false, "isPrerelease": true },
              { "tagName": "v1.4.2",        "publishedAt": "2026-04-30T00:00:00Z", "isDraft": false, "isPrerelease": false }
            ]
            """;
            var detailJson = """
            {
              "tagName": "v2.0.0-beta.1",
              "publishedAt": "2026-05-01T00:00:00Z",
              "isDraft": false,
              "isPrerelease": true,
              "assets": []
            }
            """;
            var runner = new RecordingRunner();
            runner.QueueStdout(listJson);
            runner.QueueStdout(detailJson);
            var source = new GhCliReleaseSource(Repo, includePrereleases: true, runner.RunAsync);

            var release = await source.GetLatestAsync(channel: null, CancellationToken.None);

            Assert.NotNull(release);
            Assert.Equal("v2.0.0-beta.1", release!.Tag);
            Assert.Equal("prerelease", release.Channel);

            Assert.Equal(2, runner.Invocations.Count);
            Assert.Equal("list", runner.Invocations[0][1]);
            Assert.Equal("view", runner.Invocations[1][1]);
            Assert.Contains("v2.0.0-beta.1", runner.Invocations[1]);
        }

        [Fact]
        public async Task GetLatestAsync_list_call_does_not_request_view_only_json_fields()
        {
            // Regression: `gh release list --json` rejects `url` (and other
            // fields that exist only on `release view`). Asking for them
            // returns a non-zero exit code that the source's catch-all
            // surfaces as "no result". Keep the list `--json` value restricted
            // to fields supported by `release list`.
            var runner = new RecordingRunner { NextStdout = "[]" };
            var source = new GhCliReleaseSource(Repo, includePrereleases: true, runner.RunAsync);

            await source.GetLatestAsync(channel: null, includePrereleasesOverride: null, CancellationToken.None);

            var listInvocation = runner.Invocations.Single();
            Assert.Equal("list", listInvocation[1]);

            var jsonIdx = -1;
            for (var i = 0; i < listInvocation.Count; i++)
            {
                if (listInvocation[i] == "--json") { jsonIdx = i; break; }
            }
            Assert.True(jsonIdx >= 0 && jsonIdx + 1 < listInvocation.Count);
            var fields = listInvocation[jsonIdx + 1].Split(',');
            Assert.DoesNotContain("url", fields);
            Assert.DoesNotContain("assets", fields);
        }

        [Fact]
        public async Task GetLatestAsync_when_channel_does_not_match_returns_null()
        {
            var listJson = """
            [
              { "tagName": "v1.4.2",        "publishedAt": "2026-04-30T00:00:00Z", "isDraft": false, "isPrerelease": false },
              { "tagName": "v1.4.0-alpha",  "publishedAt": "2026-04-29T00:00:00Z", "isDraft": false, "isPrerelease": true }
            ]
            """;
            var runner = new RecordingRunner { NextStdout = listJson };
            var source = new GhCliReleaseSource(Repo, includePrereleases: true, runner.RunAsync);

            var release = await source.GetLatestAsync(channel: "rc", CancellationToken.None);

            Assert.Null(release);
        }

        [Fact]
        public async Task GetLatestAsync_when_runner_throws_returns_null()
        {
            var source = new GhCliReleaseSource(Repo, includePrereleases: false,
                runner: (_, _, _) => throw new InvalidOperationException("gh missing"));

            var release = await source.GetLatestAsync(channel: null, CancellationToken.None);

            Assert.Null(release);
        }

        [Fact]
        public async Task DownloadAssetAsync_when_metadata_missing_tag_throws()
        {
            var asset = new ReleaseAsset(
                "myapp.tar.gz",
                DownloadUrl: null,
                SizeBytes: null,
                ContentType: null,
                Metadata: new Dictionary<string, string>());
            var source = new GhCliReleaseSource(Repo, includePrereleases: false,
                runner: (_, _, _) => Task.FromResult(string.Empty));

            using var ms = new MemoryStream();

            var ex = await Assert.ThrowsAsync<UpdateException>(() =>
                source.DownloadAssetAsync(asset, ms, progress: null, CancellationToken.None));
            Assert.Contains("release tag is not present", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DownloadAssetAsync_threads_tag_through_metadata_and_streams_bytes()
        {
            var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var asset = new ReleaseAsset(
                "myapp-v1.4.2-linux-x64.tar.gz",
                DownloadUrl: null,
                SizeBytes: payload.LongLength,
                ContentType: null,
                Metadata: new Dictionary<string, string>
                {
                    [GhCliReleaseSource.TagMetadataKey] = "v1.4.2",
                });

            var runner = new RecordingRunner
            {
                NextStdout = string.Empty,
                BeforeReturn = args =>
                {
                    var outputIdx = -1;
                    for (var i = 0; i < args.Count; i++)
                    {
                        if (args[i] == "--output") { outputIdx = i; break; }
                    }
                    if (outputIdx >= 0 && outputIdx + 1 < args.Count)
                    {
                        File.WriteAllBytes(args[outputIdx + 1], payload);
                    }
                },
            };
            var source = new GhCliReleaseSource(Repo, includePrereleases: false, runner.RunAsync);

            using var ms = new MemoryStream();
            await source.DownloadAssetAsync(asset, ms, progress: null, CancellationToken.None);

            Assert.Equal(payload, ms.ToArray());

            var args = runner.Invocations[0];
            Assert.Equal("release", args[0]);
            Assert.Equal("download", args[1]);
            Assert.Equal("v1.4.2", args[2]);
            Assert.Contains(Repo, args);
            Assert.Contains("myapp-v1.4.2-linux-x64.tar.gz", args);
        }

        [Fact]
        public void Constructor_rejects_blank_repository()
        {
            Assert.Throws<ArgumentException>(() => new GhCliReleaseSource("   ", includePrereleases: false));
        }

        // ---------- Test runner ----------

        /// <summary>
        /// Captures every gh argument list passed to the source so tests
        /// can assert on what was invoked, plus an optional callback that
        /// runs immediately before stdout is returned (used to simulate
        /// gh writing the asset to <c>--output</c>).
        /// </summary>
        private sealed class RecordingRunner
        {
            public List<IReadOnlyList<string>> Invocations { get; } = new();
            public Action<IReadOnlyList<string>>? BeforeReturn { get; set; }

            private readonly Queue<string> _queuedStdout = new();
            public string NextStdout
            {
                get => _queuedStdout.Count > 0 ? _queuedStdout.Peek() : string.Empty;
                set { _queuedStdout.Clear(); _queuedStdout.Enqueue(value); }
            }
            public void QueueStdout(string stdout) => _queuedStdout.Enqueue(stdout);

            public Task<string> RunAsync(IReadOnlyList<string> args, TimeSpan _, CancellationToken __)
            {
                Invocations.Add(args.ToArray());
                BeforeReturn?.Invoke(args);
                return Task.FromResult(_queuedStdout.Count > 0 ? _queuedStdout.Dequeue() : string.Empty);
            }
        }
    }
}
