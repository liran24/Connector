using System.Net;

namespace AiVisibility.Core.Http;

/// <summary>
/// Live fetcher backed by <see cref="HttpClient"/>. Identifies itself honestly so
/// merchants can see the scanner in their logs.
/// </summary>
public sealed class HttpPageFetcher : IPageFetcher
{
    public const string UserAgent =
        "AiVisibilityBot/0.1 (+https://example.com/bot; store readiness audit)";

    private const int MaxBodyBytes = 4 * 1024 * 1024;

    private readonly HttpClient _http;

    public HttpPageFetcher(HttpClient http)
    {
        _http = http;
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd(UserAgent))
        {
            _http.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        }
    }

    /// <summary>Creates a fetcher with sensible defaults for scanning a storefront.</summary>
    public static HttpPageFetcher CreateDefault(TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        };

        return new HttpPageFetcher(new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(20)
        });
    }

    public async Task<FetchResult> GetAsync(Uri url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            var body = await ReadCappedAsync(response, cancellationToken).ConfigureAwait(false);

            return new FetchResult(
                url,
                (int)response.StatusCode,
                body,
                response.Content.Headers.ContentType?.MediaType,
                Error: null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return FetchResult.Failed(url, ex.Message);
        }
    }

    /// <summary>
    /// Reads at most <see cref="MaxBodyBytes"/> so one oversized page can't exhaust memory
    /// during a bulk scan.
    /// </summary>
    private static async Task<string> ReadCappedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var buffer = new byte[MaxBodyBytes];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return System.Text.Encoding.UTF8.GetString(buffer, 0, total);
    }
}
