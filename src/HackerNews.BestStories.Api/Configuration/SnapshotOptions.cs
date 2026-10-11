using System.ComponentModel.DataAnnotations;

namespace HackerNews.BestStories.Api.Configuration;

/// <summary>Configures snapshot refresh timing, cold-start waits, and item concurrency.</summary>
public sealed class SnapshotOptions
{
    /// <summary>The configuration section containing snapshot settings.</summary>
    public const string SectionName = "Snapshot";

    /// <summary>The positive fixed timer period after the initial refresh; a pending tick follows a slow refresh immediately.</summary>
    [PositiveTimeSpan]
    public required TimeSpan RefreshInterval { get; init; }

    /// <summary>The positive maximum time a request waits for the first successful snapshot publication.</summary>
    [PositiveTimeSpan]
    public required TimeSpan InitialFetchAwaitTimeout { get; init; }

    /// <summary>The maximum concurrent item resolutions during a refresh; at least one.</summary>
    [Range(1, int.MaxValue)]
    public required int FetchParallelism { get; init; }
}
