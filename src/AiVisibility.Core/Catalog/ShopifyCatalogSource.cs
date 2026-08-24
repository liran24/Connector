using System.Text.Json;
using AiVisibility.Core.Http;

namespace AiVisibility.Core.Catalog;

/// <summary>
/// A product as the scanner needs to see it: where its page lives and what copy it carries.
/// </summary>
public sealed record CatalogProduct(string Title, string Handle, Uri Url, string BodyHtml);

/// <summary>
/// Reads a storefront's catalogue from Shopify's public <c>/products.json</c> endpoint.
/// </summary>
/// <remarks>
/// This is what makes a no-install audit possible: any shopper — and any crawler — can read
/// this endpoint, so the scanner needs no OAuth token to produce a report. Stores can disable
/// it, which the caller must handle as "catalogue unavailable" rather than "store is broken".
/// </remarks>
public sealed class ShopifyCatalogSource
{
    private readonly IPageFetcher _fetcher;

    public ShopifyCatalogSource(IPageFetcher fetcher) => _fetcher = fetcher;

    public async Task<IReadOnlyList<CatalogProduct>> GetProductsAsync(
        Uri storeUrl,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var endpoint = new Uri(storeUrl, $"/products.json?limit={limit}");
        var response = await _fetcher.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            return Array.Empty<CatalogProduct>();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(response.Body);
        }
        catch (JsonException)
        {
            return Array.Empty<CatalogProduct>();
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("products", out var products)
                || products.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<CatalogProduct>();
            }

            return products.EnumerateArray()
                .Select(p => ToProduct(p, storeUrl))
                .OfType<CatalogProduct>()
                .ToList();
        }
    }

    private static CatalogProduct? ToProduct(JsonElement element, Uri storeUrl)
    {
        if (!element.TryGetProperty("handle", out var handleNode))
        {
            return null;
        }

        var handle = handleNode.GetString();
        if (string.IsNullOrWhiteSpace(handle))
        {
            return null;
        }

        var title = element.TryGetProperty("title", out var t) ? t.GetString() ?? handle : handle;
        var body = element.TryGetProperty("body_html", out var b) ? b.GetString() ?? string.Empty : string.Empty;

        return new CatalogProduct(title, handle, new Uri(storeUrl, $"/products/{handle}"), body);
    }
}
