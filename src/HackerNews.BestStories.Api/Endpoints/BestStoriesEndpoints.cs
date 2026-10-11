using System.Globalization;
using HackerNews.BestStories.Api.Configuration;
using HackerNews.BestStories.Api.Models;
using HackerNews.BestStories.Api.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace HackerNews.BestStories.Api.Endpoints;

public static class BestStoriesEndpoints
{
    public static IEndpointRouteBuilder MapBestStoriesApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet("/api/beststories", GetBestStoriesAsync)
            .WithName("GetBestStories")
            .Produces<BestStoryDto[]>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        endpoints
            .MapGet("/health", GetHealth)
            .WithName("GetHealth")
            .Produces(StatusCodes.Status200OK, contentType: "application/json");

        return endpoints;
    }

    private static async Task<IResult> GetBestStoriesAsync(
        HttpContext context,
        SnapshotStore snapshotStore,
        IOptions<SnapshotOptions> options,
        TimeProvider timeProvider)
    {
        int? requestedCount = ParseRequestedCount(context.Request.Query);
        if (requestedCount is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid story count",
                detail: "Query parameter 'n' is required and must be a positive integer.");
        }

        SnapshotStore.SnapshotState? snapshot = snapshotStore.Current;
        if (snapshot is null)
        {
            try
            {
                await snapshotStore.InitialBuild.WaitAsync(
                    options.Value.InitialFetchAwaitTimeout,
                    timeProvider,
                    context.RequestAborted);
            }
            catch (TimeoutException)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Snapshot not available",
                    detail: "The initial Hacker News snapshot was not available within the configured timeout.");
            }

            snapshot = snapshotStore.Current!;
        }

        return Results.Ok(snapshot.Stories.Take(requestedCount.Value));
    }

    private static IResult GetHealth(SnapshotStore snapshotStore, TimeProvider timeProvider)
    {
        SnapshotStore.SnapshotState? snapshot = snapshotStore.Current;
        double? ageSeconds = snapshot is null
            ? null
            : snapshot.AgeAt(timeProvider.GetUtcNow());

        return Results.Ok(new
        {
            status = "Healthy",
            snapshotAgeSeconds = ageSeconds,
        });
    }

    private static int? ParseRequestedCount(IQueryCollection query)
    {
        if (!query.TryGetValue("n", out StringValues values) || values.Count != 1)
        {
            return null;
        }

        return int.TryParse(
            values[0],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int parsed) && parsed > 0
                ? parsed
                : null;
    }
}
