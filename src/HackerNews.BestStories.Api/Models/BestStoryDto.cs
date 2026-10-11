namespace HackerNews.BestStories.Api.Models;

/// <summary>The API response item: exactly the six contract fields in sample order.</summary>
/// <param name="Title">The story title.</param>
/// <param name="Uri">The story link, or the Hacker News item page for link-less posts.</param>
/// <param name="PostedBy">The author username.</param>
/// <param name="Time">The publication time in UTC.</param>
/// <param name="Score">The story score.</param>
/// <param name="CommentCount">The number of comments, zero when the upstream value is null.</param>
public sealed record BestStoryDto(
    string Title,
    string Uri,
    string PostedBy,
    DateTimeOffset Time,
    int Score,
    int CommentCount);
