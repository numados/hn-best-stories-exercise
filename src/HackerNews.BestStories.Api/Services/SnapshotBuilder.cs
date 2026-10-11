using System.Collections.Immutable;
using HackerNews.BestStories.Api.Mapping;
using HackerNews.BestStories.Api.Models;
using HackerNews.BestStories.Client.Models;

namespace HackerNews.BestStories.Api.Services;

public static class SnapshotBuilder
{
    public static ImmutableArray<BestStoryDto> Build(IEnumerable<HnItem> items) =>
        items
            .Where(item => item.CanMapToDto())
            .Select(item => new { item.Id, Story = item.ToDto() })
            .OrderByDescending(item => item.Story.Score)
            .ThenByDescending(item => item.Id)
            .Select(item => item.Story)
            .ToImmutableArray();
}
