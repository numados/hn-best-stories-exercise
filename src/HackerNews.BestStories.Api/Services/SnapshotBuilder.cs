using System.Collections.Immutable;
using HackerNews.BestStories.Api.Mapping;
using HackerNews.BestStories.Api.Models;
using HackerNews.BestStories.Client.Models;

namespace HackerNews.BestStories.Api.Services;

/// <summary>Builds the ordered snapshot payload from resolved items.</summary>
public static class SnapshotBuilder
{
    /// <summary>Filters out unavailable or incomplete items, maps the rest, and orders the
    /// result by score descending, then by ID descending.</summary>
    /// <returns>An immutable, response-ready array of DTOs.</returns>
    public static ImmutableArray<BestStoryDto> Build(IEnumerable<HnItem> items) =>
        items
            .Where(item => item.CanMapToDto())
            .Select(item => new { item.Id, Story = item.ToDto() })
            .OrderByDescending(item => item.Story.Score)
            .ThenByDescending(item => item.Id)
            .Select(item => item.Story)
            .ToImmutableArray();
}
