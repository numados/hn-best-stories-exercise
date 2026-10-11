using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace HackerNews.BestStories.Tests.Infrastructure;

/// <summary>
/// In-process fake of the Hacker News Firebase API. Serves canned payloads, counts every
/// outbound call (the assertion surface for cache/snapshot behavior), and can inject
/// failures per id or for the best-stories list. No network is ever touched.
/// </summary>
public sealed class FakeHackerNewsHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<int, FakeResponse> _items = new();
    private volatile FakeResponse? _idsResponse;

    public int IdListCalls;
    public int TotalItemCalls => _itemCalls.Values.Sum();
    private readonly ConcurrentDictionary<int, int> _itemCalls = new();

    public int ItemCalls(int id) => _itemCalls.GetValueOrDefault(id);

    /// <summary>Serves the given ids (duplicates allowed — used by the dedup test).</summary>
    public void SetIds(params int[] ids) =>
        _idsResponse = FakeResponse.Json(
            "[" + string.Join(",", ids.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]");

    public void SetIdsError(HttpStatusCode statusCode = HttpStatusCode.InternalServerError) =>
        _idsResponse = FakeResponse.Fail(statusCode);

    public void SetIdsRaw(string json) => _idsResponse = FakeResponse.Json(json);

    public void SetItemJson(int id, string json) => _items[id] = FakeResponse.Json(json);

    public void SetItemRawBody(int id, string rawBody) => _items[id] = FakeResponse.Raw(rawBody);

    public void SetItemError(int id, HttpStatusCode statusCode) => _items[id] = FakeResponse.Fail(statusCode);

    /// <summary>Maps the item endpoint to a successful response with an HN-style <c>null</c> body.</summary>
    public void SetItemNull(int id) => SetItemRawBody(id, "null");

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/beststories.json", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref IdListCalls);
            return Task.FromResult(CreateResponse(_idsResponse ?? FakeResponse.Json("[]")));
        }

        string? idText = Path.GetFileNameWithoutExtension(path);
        if (path.Contains("/item/", StringComparison.Ordinal)
            && int.TryParse(idText ?? "", out int id))
        {
            _itemCalls.AddOrUpdate(id, 1, static (_, count) => count + 1);
            FakeResponse response = _items.TryGetValue(id, out FakeResponse? mapped)
                ? mapped
                : FakeResponse.Raw("null");
            return Task.FromResult(CreateResponse(response));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"Unexpected path '{path}'.", Encoding.UTF8),
        });
    }

    private static HttpResponseMessage CreateResponse(FakeResponse response) => response.Error is { } statusCode
        ? new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("fake failure", Encoding.UTF8),
        }
        : new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.Body ?? "null", Encoding.UTF8, "application/json"),
        };

    private sealed record FakeResponse(string? Body, HttpStatusCode? Error)
    {
        public static FakeResponse Json(string json) => new(json, null);

        public static FakeResponse Raw(string body) => new(body, null);

        public static FakeResponse Fail(HttpStatusCode statusCode) => new(null, statusCode);
    }
}
