using AiVisibility.Core.Http;

namespace AiVisibility.Core.Tests;

/// <summary>
/// Serves canned responses by path so checks can be tested without touching the network.
/// Any path that was not registered comes back as a 404, which is what a real store does.
/// </summary>
internal sealed class FakePageFetcher : IPageFetcher
{
    private readonly Dictionary<string, FetchResult> _responses = new(StringComparer.OrdinalIgnoreCase);

    public List<Uri> RequestedUrls { get; } = new();

    public FakePageFetcher Add(string path, string body, string contentType = "text/html", int status = 200)
    {
        var url = new Uri(new Uri("https://shop.example"), path);
        _responses[url.AbsolutePath] = new FetchResult(url, status, body, contentType, Error: null);
        return this;
    }

    public Task<FetchResult> GetAsync(Uri url, CancellationToken cancellationToken = default)
    {
        RequestedUrls.Add(url);

        if (_responses.TryGetValue(url.AbsolutePath, out var response))
        {
            return Task.FromResult(response);
        }

        return Task.FromResult(new FetchResult(url, 404, string.Empty, null, Error: null));
    }
}
