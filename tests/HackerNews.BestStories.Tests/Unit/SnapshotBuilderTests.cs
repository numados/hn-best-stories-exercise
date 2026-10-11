using System.Collections.Immutable;
using HackerNews.BestStories.Api.Models;
using HackerNews.BestStories.Api.Services;
using HackerNews.BestStories.Client.Models;
using Xunit;

namespace HackerNews.BestStories.Tests.Unit;

public sealed class SnapshotBuilderTests
{
    private static HnItem Story(int id, int score, int descendants = 0) => new()
    {
        Id = id,
        Title = $"title {id}",
        By = "user",
        Time = 1_791_400_000,
        Score = score,
        Descendants = descendants,
    };

    [Fact]
    public void Builds_sorted_by_score_descending()
    {
        ImmutableArray<BestStoryDto> snapshot = SnapshotBuilder.Build(
        [
            Story(1, 100),
            Story(2, 300),
            Story(3, 200),
        ]);

        Assert.Equal([300, 200, 100], snapshot.Select(story => story.Score).ToArray());
    }

    [Fact]
    public void Equal_scores_are_ordered_by_id_descending()
    {
        ImmutableArray<BestStoryDto> snapshot = SnapshotBuilder.Build(
        [
            Story(101, 1000),
            Story(103, 1000),
            Story(102, 1000),
            Story(104, 2000),
        ]);

        Assert.Equal([104, 103, 102, 101], snapshot.Select(story => int.Parse(
            story.Uri.ToString(System.Globalization.CultureInfo.InvariantCulture).Split('=')[1])).ToArray());
    }

    [Fact]
    public void Unavailable_items_are_excluded()
    {
        ImmutableArray<BestStoryDto> snapshot = SnapshotBuilder.Build(
        [
            Story(1, 100),
            new HnItem { Id = 2, Deleted = true, By = "u", Time = 1, Score = 5 },
            new HnItem { Id = 3, Dead = true, By = "u", Time = 1, Score = 5 },
            new HnItem { Id = 4, By = "u", Time = 1, Score = 5 }, // missing title
            new HnItem { Id = 5, Title = "t", Time = 1, Score = 5 }, // missing by
            new HnItem { Id = 6, Title = "t", By = "u", Score = 5 }, // missing time
            new HnItem { Id = 7, Title = "t", By = "u", Time = 1 }, // missing score
        ]);

        Assert.Equal([100], snapshot.Select(story => story.Score).ToArray());
    }

    [Fact]
    public void Empty_input_builds_empty_snapshot()
    {
        Assert.Empty(SnapshotBuilder.Build([]));
    }
}
