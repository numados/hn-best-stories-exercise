using HackerNews.BestStories.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Time.Testing;

namespace HackerNews.BestStories.Tests.Infrastructure;

/// <summary>
/// WebApplicationFactory harness: replaces the Hacker News upstream with the in-process
/// fake, drives snapshot time through FakeTimeProvider, and shrinks timeouts/intervals so the
/// snapshot engine can be exercised by advancing virtual time. Short wall-clock polling
/// coordinates background work; upstream requests never use the network.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public ApiFactory()
    {
        Time.Advance(TimeSpan.Zero);
    }

    public FakeHackerNewsHandler HackerNews { get; } = new();

    public FakeTimeProvider Time { get; } = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

    /// <summary>Advances virtual time past one refresh interval and waits until the
    /// refresh cycle finished (observed via the fake's call counters).</summary>
    public async Task AdvanceOneRefreshCycleAsync(
        Func<bool> cycleCompleted,
        TimeSpan? timeout = null)
    {
        int idsBefore = HackerNews.IdListCalls;
        Time.Advance(TimeSpan.FromMilliseconds(1_100));
        await TestWait.UntilAsync(
            () => HackerNews.IdListCalls > idsBefore && cycleCompleted(),
            timeout ?? TimeSpan.FromSeconds(30));
    }

    public Task AwaitInitialSnapshotAsync() =>
        Services.GetRequiredService<SnapshotStore>()
            .InitialBuild.WaitAsync(TimeSpan.FromSeconds(30));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Fast intervals and a bounded retry count keep the suite quick; the
        // circuit breaker is moved out of the way for failure-injection tests.
        builder.UseSetting("Snapshot:RefreshInterval", "00:00:01");
        builder.UseSetting("Snapshot:InitialFetchAwaitTimeout", "00:00:02");
        builder.UseSetting("Resilience:RetryCount", "1");
        builder.UseSetting("Resilience:AttemptTimeout", "00:00:02");
        builder.UseSetting("Resilience:TotalTimeout", "00:00:05");
        builder.UseSetting("Resilience:CircuitBreakerMinimumThroughput", "100000");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
                    handlerBuilder.PrimaryHandler = HackerNews));
        });
    }
}

public static class TestWait
{
    /// <summary>Polls in-process state with short wall-clock delays until it holds or
    /// a wall-clock timeout expires. Virtual time drives the snapshot engine itself.</summary>
    public static async Task UntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"Condition was not met within {timeout}. Current state did not satisfy the assertion.");
            }

            await Task.Delay(10);
        }
    }
}
