using System.Net;
using System.Text.Json;
using HackerNews.BestStories.Tests.Infrastructure;
using Xunit;

namespace HackerNews.BestStories.Tests.Integration;

/// <summary>HTTP contract tests: shape, ordering,
/// validation, truncation — end-to-end through the real pipeline with a fake upstream.</summary>
public sealed class BestStoriesContractTests : IAsyncDisposable
{
    private readonly ApiFactory _factory = new();

    private static (int Id, int Score)[] ExpectedOrder()
    {
        int[] ids = Fixtures.LiveStoryIds.ToArray();
        return ids
            .Select(id =>
            {
                using JsonDocument document = Fixtures.LoadJson($"item-{id}.json");
                return (Id: id, Score: document.RootElement.GetProperty("score").GetInt32());
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Id)
            .ToArray();
    }

    private static async Task<JsonElement> GetArrayAsync(HttpClient client, string query)
    {
        using HttpResponseMessage response = await client.GetAsync($"/api/beststories?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement;
    }

    [Fact]
    public async Task N_stories_return_exact_shape_and_order()
    {
        Fixtures.SeedLiveFixtures(_factory.HackerNews);
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();

        JsonElement array = await GetArrayAsync(client, "n=5");
        (int Id, int Score)[] expected = ExpectedOrder().Take(5).ToArray();

        Assert.Equal(5, array.GetArrayLength());
        int index = 0;
        foreach (JsonElement story in array.EnumerateArray())
        {
            using JsonDocument fixture = Fixtures.LoadJson($"item-{expected[index].Id}.json");
            JsonElement source = fixture.RootElement;
            Assert.Equal(
                ["title", "uri", "postedBy", "time", "score", "commentCount"],
                story.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.Equal(source.GetProperty("title").GetString(), story.GetProperty("title").GetString());
            Assert.Equal(source.GetProperty("url").GetString(), story.GetProperty("uri").GetString());
            Assert.Equal(source.GetProperty("by").GetString(), story.GetProperty("postedBy").GetString());
            Assert.Equal(source.GetProperty("score").GetInt32(), story.GetProperty("score").GetInt32());
            Assert.Equal(source.GetProperty("descendants").GetInt32(), story.GetProperty("commentCount").GetInt32());
            index++;
        }
    }

    [Fact]
    public async Task Time_is_iso8601_with_explicit_utc_offset()
    {
        Fixtures.SeedLiveFixtures(_factory.HackerNews);
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();

        JsonElement array = await GetArrayAsync(client, "n=6");
        Assert.NotEmpty(array.EnumerateArray());
        foreach (JsonElement story in array.EnumerateArray())
        {
            Assert.Matches(
                @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\+00:00$",
                story.GetProperty("time").GetString());
        }
    }

    [Fact]
    public async Task Equal_scores_are_ordered_by_newer_story_first()
    {
        for (int id = 101; id <= 103; id++)
        {
            _factory.HackerNews.SetItemJson(id, $$"""
                {"id":{{id}},"by":"user","time":1791400000,"type":"story",
                 "title":"tied {{id}}","score":1000,"descendants":0}
                """);
        }
        _factory.HackerNews.SetIds(101, 102, 103);
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();

        JsonElement array = await GetArrayAsync(client, "n=3");

        Assert.Equal(
            [103, 102, 101],
            array.EnumerateArray()
                .Select(story => int.Parse(
                    story.GetProperty("uri").GetString()!.Split('=')[1],
                    System.Globalization.CultureInfo.InvariantCulture))
                .ToArray());
    }

    [Theory]
    [InlineData("")]                      // missing n entirely
    [InlineData("n=0")]
    [InlineData("n=-3")]
    [InlineData("n=abc")]
    [InlineData("n=99999999999")]         // int overflow
    public async Task Invalid_n_returns_400_problem_details(string query)
    {
        Fixtures.SeedLiveFixtures(_factory.HackerNews);
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();

        using HttpResponseMessage response = await client.GetAsync($"/api/beststories?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task N_above_available_returns_all_stories()
    {
        Fixtures.SeedLiveFixtures(_factory.HackerNews);
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();

        JsonElement array = await GetArrayAsync(client, $"n={int.MaxValue}");

        Assert.Equal(ExpectedOrder().Length, array.GetArrayLength());
    }

    [Fact]
    public async Task Post_without_url_gets_hacker_news_item_link()
    {
        Fixtures.SeedLiveFixtures(_factory.HackerNews); // includes genuine Tell HN post 50006366
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();

        JsonElement array = await GetArrayAsync(client, "n=6");

        JsonElement? tellHn = array.EnumerateArray().SingleOrDefault(story =>
            story.GetProperty("uri").GetString()
                == "https://news.ycombinator.com/item?id=50006366");
        Assert.NotNull(tellHn);
    }

    [Fact]
    public async Task Duplicate_ids_are_deduplicated()
    {
        _factory.HackerNews.SetIds(1, 2, 3, 2, 1);
        foreach (int id in new[] { 1, 2, 3 })
        {
            _factory.HackerNews.SetItemJson(id, $$"""
                {"id":{{id}},"by":"user","time":1791400000,"type":"story",
                 "title":"story {{id}}","score":{{id * 100}},"descendants":0}
                """);
        }
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();

        JsonElement array = await GetArrayAsync(client, "n=10");

        int[] ids = array.EnumerateArray()
            .Select(story => int.Parse(
                story.GetProperty("uri").GetString()!.Split('=')[1],
                System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        Assert.Equal([3, 2, 1], ids);
    }

    [Fact]
    public async Task Empty_best_stories_list_returns_empty_array()
    {
        _factory.HackerNews.SetIds();
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();

        JsonElement array = await GetArrayAsync(client, "n=5");

        Assert.Equal(0, array.GetArrayLength());
    }

    [Fact]
    public async Task Post_method_is_rejected()
    {
        Fixtures.SeedLiveFixtures(_factory.HackerNews);
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();

        using HttpResponseMessage response = await client.PostAsync(
            "/api/beststories?n=5", content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Parallel_different_n_share_snapshot_and_truncate_independently()
    {
        Fixtures.SeedLiveFixtures(_factory.HackerNews);
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();

        HttpClient singleton = client;
        int storyCount = Fixtures.LiveStoryIds.Length;
        Task<JsonElement>[] requests = Enumerable.Range(1, storyCount)
            .Select(n => GetArrayAsync(singleton, $"n={n}"))
            .ToArray();
        JsonElement[] responses = await Task.WhenAll(requests);

        for (int n = 1; n <= storyCount; n++)
        {
            Assert.Equal(n, responses[n - 1].GetArrayLength());
        }

        JsonElement full = responses[storyCount - 1];
        for (int n = 1; n <= storyCount; n++)
        {
            Assert.Equal(
                full.EnumerateArray().Take(n).Select(story => story.GetProperty("title").GetString()),
                responses[n - 1].EnumerateArray().Select(story => story.GetProperty("title").GetString()));
        }
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
