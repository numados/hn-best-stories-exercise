using System.Text.Json;
using HackerNews.BestStories.Api.Mapping;
using HackerNews.BestStories.Client.Models;
using HackerNews.BestStories.Tests.Infrastructure;
using Xunit;

namespace HackerNews.BestStories.Tests.Unit;

public sealed class MappingTests
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void ToDto_maps_live_fixture_field_by_field()
    {
        HnItem item = Fixtures.Load<HnItem>("item-49998895.json");
        using JsonDocument fixture = Fixtures.LoadJson("item-49998895.json");
        JsonElement root = fixture.RootElement;

        var dto = item.ToDto();

        Assert.Equal(root.GetProperty("title").GetString(), dto.Title);
        Assert.Equal(root.GetProperty("url").GetString(), dto.Uri);
        Assert.Equal(root.GetProperty("by").GetString(), dto.PostedBy);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("time").GetInt64()),
            dto.Time);
        Assert.Equal(root.GetProperty("score").GetInt32(), dto.Score);
        Assert.Equal(root.GetProperty("descendants").GetInt32(), dto.CommentCount);
    }

    [Fact]
    public void Serialized_dto_has_exactly_six_fields_in_sample_order_with_exact_time_token()
    {
        HnItem item = Fixtures.Load<HnItem>("item-49998895.json");

        string json = JsonSerializer.Serialize(item.ToDto(), SerializerOptions);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(
            ["title", "uri", "postedBy", "time", "score", "commentCount"],
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Matches(
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\+00:00$",
            document.RootElement.GetProperty("time").GetString());
    }

    [Fact]
    public void ToDto_falls_back_to_hn_item_link_when_url_is_missing()
    {
        HnItem item = Fixtures.Load<HnItem>("item-50006366.json");
        Assert.Null(item.Url); // genuine Tell HN post without url

        var dto = item.ToDto();

        Assert.Equal("https://news.ycombinator.com/item?id=50006366", dto.Uri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToDto_falls_back_to_hn_item_link_for_blank_urls(string? url)
    {
        HnItem item = new()
        {
            Id = 7,
            Title = "title",
            By = "user",
            Time = 1_791_400_000,
            Score = 10,
            Url = url,
        };

        Assert.Equal("https://news.ycombinator.com/item?id=7", item.ToDto().Uri);
    }

    [Fact]
    public void ToDto_maps_missing_descendants_to_zero()
    {
        HnItem item = new()
        {
            Id = 8,
            Title = "title",
            By = "user",
            Time = 1_791_400_000,
            Score = 10,
        };

        Assert.Equal(0, item.ToDto().CommentCount);
    }

    [Fact]
    public void Items_missing_required_fields_cannot_be_mapped()
    {
        HnItem complete = new()
        {
            Id = 9,
            Title = "title",
            By = "user",
            Time = 1_791_400_000,
            Score = 10,
        };
        Assert.True(complete.CanMapToDto());

        Assert.False(new HnItem
        {
            Id = 10,
            By = "user",
            Time = 1,
            Score = 1,
        }.CanMapToDto()); // missing title
        Assert.False(new HnItem
        {
            Id = 11,
            Title = "t",
            Time = 1,
            Score = 1,
        }.CanMapToDto()); // missing by
        Assert.False(new HnItem
        {
            Id = 12,
            Title = "t",
            By = "u",
            Score = 1,
        }.CanMapToDto()); // missing time
        Assert.False(new HnItem
        {
            Id = 13,
            Title = "t",
            By = "u",
            Time = 1,
        }.CanMapToDto()); // missing score
        Assert.False(new HnItem
        {
            Id = 14,
            Title = "t",
            By = "u",
            Time = 1,
            Score = 1,
            Deleted = true,
        }.CanMapToDto()); // deleted
        Assert.False(new HnItem
        {
            Id = 15,
            Title = "t",
            By = "u",
            Time = 1,
            Score = 1,
            Dead = true,
        }.CanMapToDto()); // dead
    }

    [Fact]
    public void Zero_score_is_a_valid_score_not_a_missing_one()
    {
        HnItem item = new()
        {
            Id = 16,
            Title = "title",
            By = "user",
            Time = 1_791_400_000,
            Score = 0,
        };

        Assert.True(item.CanMapToDto());
        Assert.Equal(0, item.ToDto().Score);
    }
}
