using HackerNews.BestStories.Api.Configuration;
using HackerNews.BestStories.Api.Endpoints;
using HackerNews.BestStories.Api.Services;
using HackerNews.BestStories.Api.Services.Caching;
using HackerNews.BestStories.Client;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<SnapshotOptions>()
    .Bind(builder.Configuration.GetRequiredSection(SnapshotOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services
    .AddOptions<ResilienceOptions>()
    .Bind(builder.Configuration.GetRequiredSection(ResilienceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddStoryCache(builder.Configuration);
builder.Services.AddSingleton<SnapshotStore>();
builder.Services.AddHostedService<BestStoriesSnapshotService>();

builder.Services
    .AddHttpClient<HackerNewsClient>(client => client.BaseAddress = HackerNewsClient.DefaultBaseAddress)
    .AddStandardResilienceHandler()
    .Configure((options, services) =>
    {
        ResilienceOptions resilience = services.GetRequiredService<IOptions<ResilienceOptions>>().Value;
        options.AttemptTimeout.Timeout = resilience.AttemptTimeout;
        options.TotalRequestTimeout.Timeout = resilience.TotalTimeout;
        options.Retry.MaxRetryAttempts = resilience.RetryCount;
        options.Retry.UseJitter = true;
        options.CircuitBreaker.FailureRatio = resilience.CircuitBreakerFailureRatio;
        options.CircuitBreaker.SamplingDuration = resilience.CircuitBreakerSamplingWindow;
        options.CircuitBreaker.BreakDuration = resilience.CircuitBreakerBreakDuration;
        options.CircuitBreaker.MinimumThroughput = resilience.CircuitBreakerMinimumThroughput;
    });

WebApplication app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapBestStoriesApi();

app.Run();

public partial class Program;
