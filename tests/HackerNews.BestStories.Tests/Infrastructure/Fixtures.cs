using System.Collections.Immutable;
using System.Text.Json;

namespace HackerNews.BestStories.Tests.Infrastructure;

/// <summary>Loads golden fixtures captured from the live Hacker News API
/// (see <c>Fixtures/hn/README.md</c> for provenance).</summary>
public static class Fixtures
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static readonly ImmutableArray<int> LiveStoryIds =
        Load<int[]>("beststories.json").Take(6).ToImmutableArray();

    public static void SeedLiveFixtures(FakeHackerNewsHandler hn)
    {
        hn.SetIds([.. LiveStoryIds]);
        foreach (int id in LiveStoryIds)
        {
            hn.SetItemJson(id, LoadText($"item-{id}.json"));
        }
    }

    public static string LoadText(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "hn", fileName));

    public static JsonDocument LoadJson(string fileName) =>
        JsonDocument.Parse(LoadText(fileName));

    public static T Load<T>(string fileName) =>
        JsonSerializer.Deserialize<T>(LoadText(fileName), Options)
            ?? throw new InvalidOperationException($"Fixture '{fileName}' deserialized to null.");
}
