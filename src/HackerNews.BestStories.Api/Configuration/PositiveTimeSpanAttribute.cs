using System.ComponentModel.DataAnnotations;

namespace HackerNews.BestStories.Api.Configuration;

/// <summary>Validates that a TimeSpan is positive; rejects zero and negative values.</summary>
/// <remarks>
/// The bounds are one tick and <see cref="TimeSpan.MaxValue"/> rendered as text —
/// <c>RangeAttribute</c> has no minimum-only constructor. The upper bound is not a
/// policy limit; it is simply the largest value the type can hold.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PositiveTimeSpanAttribute() : RangeAttribute(
    typeof(TimeSpan), "00:00:00.0000001", "10675199.02:48:05.4775807");
