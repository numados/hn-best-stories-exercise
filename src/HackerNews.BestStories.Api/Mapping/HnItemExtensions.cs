using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using HackerNews.BestStories.Api.Models;
using HackerNews.BestStories.Client.Models;

namespace HackerNews.BestStories.Api.Mapping;

public static class HnItemExtensions
{
    /// <summary>Checks the upstream deleted and dead flags.</summary>
    /// <returns>True when either flag is true.</returns>
    public static bool IsUnavailable(this HnItem item) => item.Deleted is true || item.Dead is true;

    public static bool CanMapToDto(this HnItem item) => TryGetRequiredValues(
        item,
        out _,
        out _,
        out _,
        out _);

    public static BestStoryDto ToDto(this HnItem item)
    {
        if (!TryGetRequiredValues(
            item,
            out string? title,
            out string? postedBy,
            out long time,
            out int score))
        {
            throw new InvalidOperationException($"Item {item.Id} is not a complete, available story.");
        }

        string uri = string.IsNullOrWhiteSpace(item.Url)
            ? $"https://news.ycombinator.com/item?id={item.Id.ToString(CultureInfo.InvariantCulture)}"
            : item.Url;

        return new BestStoryDto(
            title,
            uri,
            postedBy,
            DateTimeOffset.FromUnixTimeSeconds(time),
            score,
            item.Descendants ?? 0);
    }

    private static bool TryGetRequiredValues(
        HnItem item,
        [NotNullWhen(true)] out string? title,
        [NotNullWhen(true)] out string? postedBy,
        out long time,
        out int score)
    {
        title = item.Title;
        postedBy = item.By;
        time = item.Time.GetValueOrDefault();
        score = item.Score.GetValueOrDefault();

        return !item.IsUnavailable() &&
            title is not null &&
            postedBy is not null &&
            item.Time.HasValue &&
            item.Score.HasValue;
    }
}
