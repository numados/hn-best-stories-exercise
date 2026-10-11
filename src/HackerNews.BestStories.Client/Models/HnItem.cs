using System.Text.Json.Serialization;

namespace HackerNews.BestStories.Client.Models;

/// <summary>An item payload from the Hacker News Firebase API. Deleted and dead items carry
/// only their flags, and optional fields may be absent on any response.</summary>
public sealed class HnItem
{
    /// <summary>The unique item ID; always present.</summary>
    [JsonPropertyName("id")]
    public int Id { get; init; }

    /// <summary>True when the item was deleted; such responses omit most other fields.</summary>
    [JsonPropertyName("deleted")]
    public bool? Deleted { get; init; }

    /// <summary>The author username.</summary>
    [JsonPropertyName("by")]
    public string? By { get; init; }

    /// <summary>The creation time in Unix seconds.</summary>
    [JsonPropertyName("time")]
    public long? Time { get; init; }

    /// <summary>The item type, such as story.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The story title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>The external link; absent for Ask and Tell posts.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>The story score.</summary>
    [JsonPropertyName("score")]
    public int? Score { get; init; }

    /// <summary>The number of comments.</summary>
    [JsonPropertyName("descendants")]
    public int? Descendants { get; init; }

    /// <summary>True when the item was flagged dead; treated like deleted.</summary>
    [JsonPropertyName("dead")]
    public bool? Dead { get; init; }
}
