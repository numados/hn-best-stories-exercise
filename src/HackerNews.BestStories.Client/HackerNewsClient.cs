using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HackerNews.BestStories.Client.Models;

namespace HackerNews.BestStories.Client;

/// <summary>Reads best-story IDs and item payloads from the Hacker News Firebase API.</summary>
public sealed class HackerNewsClient(HttpClient httpClient)
{
    /// <summary>The default Hacker News Firebase API base address.</summary>
    public static readonly Uri DefaultBaseAddress = new("https://hacker-news.firebaseio.com/");

    /// <summary>Fetches the best-story ID list; unsuccessful responses and JSON null lists throw.</summary>
    /// <returns>The IDs in upstream order, including any duplicates.</returns>
    public async Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            "v0/beststories.json",
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<int[]>(cancellationToken)
            ?? throw new JsonException("The Hacker News best-stories response was null.");
    }

    /// <summary>Fetches an item; unsuccessful responses other than HTTP 404 and 410 throw.</summary>
    /// <returns>The item, or null for HTTP 404, HTTP 410, or a JSON null body.</returns>
    public async Task<HnItem?> GetItemAsync(int id, CancellationToken cancellationToken)
    {
        string itemId = id.ToString(CultureInfo.InvariantCulture);
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"v0/item/{itemId}.json",
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<HnItem>(cancellationToken);
    }
}
