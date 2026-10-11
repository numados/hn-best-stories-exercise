using System.ComponentModel.DataAnnotations;

namespace HackerNews.BestStories.Api.Configuration;

/// <summary>Configures per-call HTTP timeouts, retries, and circuit breaking.</summary>
public sealed class ResilienceOptions : IValidatableObject
{
    /// <summary>The configuration section containing HTTP resilience settings.</summary>
    public const string SectionName = "Resilience";

    /// <summary>The positive timeout for one upstream HTTP attempt.</summary>
    [PositiveTimeSpan]
    public required TimeSpan AttemptTimeout { get; init; }

    /// <summary>The positive timeout for the whole operation, including retries; at least AttemptTimeout.</summary>
    [PositiveTimeSpan]
    public required TimeSpan TotalTimeout { get; init; }

    /// <summary>The number of retries after the initial HTTP attempt (1–100, the range the standard resilience handler accepts).</summary>
    [Range(1, 100)]
    public required int RetryCount { get; init; }

    /// <summary>The positive sampled failure ratio, up to one, that can open the circuit.</summary>
    [Range(0.0000001, 1.0)]
    public required double CircuitBreakerFailureRatio { get; init; }

    /// <summary>The positive window over which circuit-breaker outcomes are sampled.</summary>
    [PositiveTimeSpan]
    public required TimeSpan CircuitBreakerSamplingWindow { get; init; }

    /// <summary>The positive duration for which an open circuit rejects calls before probing.</summary>
    [PositiveTimeSpan]
    public required TimeSpan CircuitBreakerBreakDuration { get; init; }

    /// <summary>The minimum number of sampled calls, at least two, before the circuit can open.</summary>
    [Range(2, int.MaxValue)]
    public required int CircuitBreakerMinimumThroughput { get; init; }

    /// <summary>Validates the relationship between total and per-attempt timeouts.</summary>
    /// <returns>A validation error when TotalTimeout is shorter than AttemptTimeout.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TotalTimeout < AttemptTimeout)
        {
            yield return new ValidationResult(
                $"{nameof(TotalTimeout)} must be greater than or equal to {nameof(AttemptTimeout)}.",
                [nameof(TotalTimeout), nameof(AttemptTimeout)]);
        }
    }
}
