using System.ComponentModel.DataAnnotations;
using HackerNews.BestStories.Api.Configuration;
using Microsoft.Extensions.Options;

namespace HackerNews.BestStories.Api.Services.Caching;

public static class ServiceCollectionExtensions
{
    /// <summary>The connection string that backs the distributed story cache.</summary>
    public const string RedisConnectionStringName = "Redis";

    public static IServiceCollection AddStoryCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        CacheOptions options = configuration.GetRequiredSection(CacheOptions.SectionName)
            .Get<CacheOptions>()
            ?? throw new InvalidOperationException("Cache configuration is missing.");
        Validator.ValidateObject(options, new ValidationContext(options), validateAllProperties: true);
        services.AddSingleton(Options.Create(options));

        switch (options.Provider)
        {
            case CacheOptions.InMemoryProvider:
                services.AddMemoryCache();
                services.AddSingleton<IStoryCache, InMemoryStoryCache>();
                break;

            case CacheOptions.DistributedProvider:
                string redis = configuration.GetConnectionString(RedisConnectionStringName)
                    ?? throw new InvalidOperationException(
                        $"Cache:Provider={CacheOptions.DistributedProvider} requires " +
                        $"ConnectionStrings:{RedisConnectionStringName}.");
                services.AddStackExchangeRedisCache(options => options.Configuration = redis);
                services.AddSingleton<IStoryCache, DistributedStoryCache>();
                break;
        }

        return services;
    }
}
