using System.Net;
using System.Text.Json;
using HackerNews.BestStories.Client;
using HackerNews.BestStories.Tests.Infrastructure;
using Xunit;

namespace HackerNews.BestStories.Tests.Unit;

public sealed class HackerNewsClientTests
{
    private static HttpClient CreateHttpClient(FakeHackerNewsHandler handler)
    {
        HttpClient httpClient = new(handler)
        {
            BaseAddress = HackerNewsClient.DefaultBaseAddress,
        };
        return httpClient;
    }

    [Fact]
    public async Task GetBestStoryIdsAsync_parses_live_fixture()
    {
        FakeHackerNewsHandler handler = new();
        handler.SetIdsRaw(Fixtures.LoadText("beststories.json"));
        using HttpClient httpClient = CreateHttpClient(handler);
        HackerNewsClient client = new(httpClient);

        IReadOnlyList<int> ids = await client.GetBestStoryIdsAsync(CancellationToken.None);

        Assert.Equal(Fixtures.Load<int[]>("beststories.json").Length, ids.Count);
        Assert.Equal(Fixtures.Load<int[]>("beststories.json")[0], ids[0]);
    }

    [Fact]
    public async Task GetItemAsync_parses_live_fixture_field_by_field()
    {
        FakeHackerNewsHandler handler = new();
        handler.SetItemJson(49998895, Fixtures.LoadText("item-49998895.json"));
        using HttpClient httpClient = CreateHttpClient(handler);
        HackerNewsClient client = new(httpClient);

        Client.Models.HnItem item =
            await client.GetItemAsync(49998895, CancellationToken.None)
            ?? throw new Xunit.Sdk.XunitException("Expected a parsed item.");

        using JsonDocument fixture = Fixtures.LoadJson("item-49998895.json");
        JsonElement root = fixture.RootElement;
        Assert.Equal(root.GetProperty("id").GetInt32(), item.Id);
        Assert.Equal(root.GetProperty("title").GetString(), item.Title);
        Assert.Equal(root.GetProperty("by").GetString(), item.By);
        Assert.Equal(root.GetProperty("time").GetInt64(), item.Time);
        Assert.Equal(root.GetProperty("score").GetInt32(), item.Score);
        Assert.Equal(root.GetProperty("descendants").GetInt32(), item.Descendants);
        Assert.Equal(root.GetProperty("url").GetString(), item.Url);
        Assert.Null(item.Deleted);
        Assert.Null(item.Dead);
    }

    [Fact]
    public async Task GetItemAsync_tolerates_optional_fields()
    {
        FakeHackerNewsHandler handler = new();
        handler.SetItemJson(47, Fixtures.LoadText("item-no-descendants.json"));
        using HttpClient httpClient = CreateHttpClient(handler);
        HackerNewsClient client = new(httpClient);

        Client.Models.HnItem item = await client.GetItemAsync(47, CancellationToken.None)
            ?? throw new Xunit.Sdk.XunitException("Expected a parsed item.");

        Assert.Null(item.Descendants);
        Assert.Null(item.Deleted);
        Assert.Null(item.Dead);
    }

    [Fact]
    public async Task GetItemAsync_parses_deleted_and_dead_flags()
    {
        FakeHackerNewsHandler handler = new();
        handler.SetItemJson(45, Fixtures.LoadText("item-deleted.json"));
        handler.SetItemJson(46, Fixtures.LoadText("item-dead.json"));
        using HttpClient httpClient = CreateHttpClient(handler);
        HackerNewsClient client = new(httpClient);

        Client.Models.HnItem deleted =
            await client.GetItemAsync(45, CancellationToken.None)
            ?? throw new Xunit.Sdk.XunitException("Expected a parsed item.");
        Client.Models.HnItem dead =
            await client.GetItemAsync(46, CancellationToken.None)
            ?? throw new Xunit.Sdk.XunitException("Expected a parsed item.");

        Assert.True(deleted.Deleted);
        Assert.True(dead.Dead);
    }

    [Theory]
    [InlineData(456)]
    public async Task GetItemAsync_returns_null_for_null_body(int id)
    {
        FakeHackerNewsHandler handler = new();
        handler.SetItemNull(id);
        using HttpClient httpClient = CreateHttpClient(handler);
        HackerNewsClient client = new(httpClient);

        Assert.Null(await client.GetItemAsync(id, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task GetItemAsync_returns_null_for_404_and_410(HttpStatusCode statusCode)
    {
        FakeHackerNewsHandler handler = new();
        handler.SetItemError(789, statusCode);
        using HttpClient httpClient = CreateHttpClient(handler);
        HackerNewsClient client = new(httpClient);

        Assert.Null(await client.GetItemAsync(789, CancellationToken.None));
    }

    [Fact]
    public async Task GetItemAsync_throws_for_server_errors()
    {
        FakeHackerNewsHandler handler = new();
        handler.SetItemError(1, HttpStatusCode.InternalServerError);
        using HttpClient httpClient = CreateHttpClient(handler);
        HackerNewsClient client = new(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetItemAsync(1, CancellationToken.None));
    }
}
