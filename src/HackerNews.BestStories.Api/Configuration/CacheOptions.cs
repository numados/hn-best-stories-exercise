using System.ComponentModel.DataAnnotations;

namespace HackerNews.BestStories.Api.Configuration;

/// <summary>Selects the story cache and the absolute lifetimes of its entries.</summary>
public sealed class CacheOptions : IValidatableObject
{
    /// <summary>The configuration section containing cache settings.</summary>
    public const string SectionName = "Cache";
    /// <summary>The provider name for the process-local memory story cache.</summary>
    public const string InMemoryProvider = "InMemory";

    /// <summary>The provider name for the story cache backed by a registered IDistributedCache.</summary>
    public const string DistributedProvider = "Distributed";

    /// <summary>The story-cache provider: InMemory or Distributed; Distributed requires a backend registration.</summary>
    [Required]
    public required string Provider { get; init; }

    /// <summary>The positive absolute lifetime from a story cache write; reads do not extend it.</summary>
    [PositiveTimeSpan]
    public required TimeSpan ItemTtl { get; init; }

    /// <summary>The positive absolute lifetime from an unavailable cache write; reads do not extend it.</summary>
    [PositiveTimeSpan]
    public required TimeSpan NegativeTtl { get; init; }

    /// <summary>Validates the supported story-cache provider names.</summary>
    /// <returns>A validation error when the provider name is unsupported.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provider is not InMemoryProvider and not DistributedProvider)
        {
            yield return new ValidationResult(
                $"{nameof(Provider)} must be '{InMemoryProvider}' or '{DistributedProvider}'.",
                [nameof(Provider)]);
        }
    }
}
