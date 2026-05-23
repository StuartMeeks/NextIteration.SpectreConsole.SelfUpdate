using System.Text.Json;
using System.Text.Json.Serialization;

namespace NextIteration.SpectreConsole.SelfUpdate.Pipeline
{
    /// <summary>
    /// Persists the result of the most recent "what's the latest tag?"
    /// probe so subsequent invocations within the cache TTL can answer the
    /// question without hitting the source. Failures (read/write/parse) are
    /// non-fatal — the checker treats a missing or corrupt cache the same
    /// as no cache.
    /// </summary>
    internal static class UpdateCacheFile
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };

        public static UpdateCacheEntry? TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<UpdateCacheEntry>(json, JsonOpts);
            }
            catch
            {
                return null;
            }
        }

        public static void TryWrite(string path, UpdateCacheEntry entry)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(entry, JsonOpts));
            }
            catch
            {
                // Cache-write failures are non-fatal — the source will be
                // hit again on the next invocation.
            }
        }
    }

    internal sealed record UpdateCacheEntry(
        [property: JsonPropertyName("checkedAt")] DateTimeOffset CheckedAt,
        [property: JsonPropertyName("latestTag")] string LatestTag,
        [property: JsonPropertyName("releaseUrl")] string? ReleaseUrl,
        [property: JsonPropertyName("channel")] string? Channel,
        [property: JsonPropertyName("includePrereleases")] bool? IncludePrereleases = null);
}
