using System.Text.Json;
using AiVisibility.Core.Models;

namespace AiVisibility.Core.StructuredData;

/// <summary>
/// One schema.org property an agent needs, and what its absence costs.
/// </summary>
/// <param name="Path">Dotted path within the Product node, e.g. "offers.availability".</param>
/// <param name="Label">Merchant-facing name of the field.</param>
/// <param name="WhyItMatters">The shopper question the field answers.</param>
/// <param name="Alternatives">Other paths that satisfy the same need (e.g. sku or gtin13).</param>
public sealed record SchemaField(
    string Path,
    string Label,
    Severity Severity,
    string WhyItMatters,
    params string[] Alternatives);

/// <summary>
/// Grades a page's <c>Product</c> markup against the fields AI shopping agents rely on
/// when deciding whether they can recommend an item.
/// </summary>
public static class ProductSchemaAnalyzer
{
    /// <summary>
    /// The fields that matter, ordered by how much an agent needs them. Critical fields are
    /// the ones without which an agent cannot state a price, a fit, or an availability —
    /// so it recommends a competitor instead.
    /// </summary>
    public static IReadOnlyList<SchemaField> Fields { get; } = new[]
    {
        new SchemaField("name", "Product name", Severity.Critical,
            "an agent cannot name the product it is recommending"),
        new SchemaField("offers.price", "Price", Severity.Critical,
            "shoppers filter by budget; an agent skips products it cannot price"),
        new SchemaField("offers.priceCurrency", "Currency", Severity.Critical,
            "without a currency the price is ambiguous and gets dropped"),
        new SchemaField("offers.availability", "Availability", Severity.Critical,
            "agents avoid recommending items they cannot confirm are in stock"),
        new SchemaField("image", "Product image", Severity.Critical,
            "shopping surfaces that render cards omit products with no image"),

        new SchemaField("description", "Description", Severity.Important,
            "the description is what an agent paraphrases when it explains the product"),
        new SchemaField("brand", "Brand", Severity.Important,
            "brand-name queries are among the most common shopping prompts"),
        new SchemaField("sku", "Product identifier", Severity.Important,
            "identifiers let agents match this listing to the same product elsewhere",
            "gtin", "gtin8", "gtin12", "gtin13", "gtin14", "mpn", "productID"),
        new SchemaField("aggregateRating", "Ratings", Severity.Important,
            "agents lean on ratings when ranking otherwise similar products",
            "review"),

        new SchemaField("offers.shippingDetails", "Shipping details", Severity.Minor,
            "delivery time is a frequent follow-up question agents try to answer"),
        new SchemaField("offers.hasMerchantReturnPolicy", "Return policy", Severity.Minor,
            "return terms influence agent recommendations for higher-priced items"),
        new SchemaField("color", "Variant attributes", Severity.Minor,
            "attribute queries ('in navy', 'size 10') need structured variants",
            "size", "material", "additionalProperty", "hasVariant")
    };

    /// <summary>
    /// Checks one product page's markup.
    /// </summary>
    /// <returns>
    /// The <c>Product</c> node's missing fields, or null when the page has no Product markup
    /// at all — a materially different problem the caller reports separately.
    /// </returns>
    public static IReadOnlyList<SchemaField>? FindMissingFields(string html)
    {
        var nodes = JsonLdExtractor.Extract(html);
        var product = JsonLdExtractor.FindByType(nodes, "Product")
            ?? JsonLdExtractor.FindByType(nodes, "ProductGroup");

        if (product is null)
        {
            return null;
        }

        return Fields.Where(field => !IsSatisfied(product.Value, field)).ToList();
    }

    private static bool IsSatisfied(JsonElement product, SchemaField field) =>
        HasValue(product, field.Path) || field.Alternatives.Any(alt => HasValue(product, SiblingPath(field, alt)));

    /// <summary>
    /// Alternatives are expressed as bare property names; nest them under the same parent as
    /// the primary path so "offers.price" can fall back to a sibling of <c>offers</c>.
    /// </summary>
    private static string SiblingPath(SchemaField field, string alternative)
    {
        var lastDot = field.Path.LastIndexOf('.');
        return lastDot < 0 ? alternative : $"{field.Path[..lastDot]}.{alternative}";
    }

    /// <summary>
    /// Resolves a dotted path, treating arrays as "any element satisfies it" — a product
    /// with several offers only needs one of them priced.
    /// </summary>
    private static bool HasValue(JsonElement node, string path)
    {
        var separator = path.IndexOf('.');
        var head = separator < 0 ? path : path[..separator];
        var tail = separator < 0 ? null : path[(separator + 1)..];

        if (node.ValueKind == JsonValueKind.Array)
        {
            return node.EnumerateArray().Any(item => HasValue(item, path));
        }

        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(head, out var child))
        {
            return false;
        }

        return tail is null ? IsMeaningful(child) : HasValue(child, tail);
    }

    /// <summary>
    /// A present-but-empty property is the same as a missing one to an agent, so empty
    /// strings, empty arrays, and empty objects do not count.
    /// </summary>
    private static bool IsMeaningful(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Array => value.EnumerateArray().Any(IsMeaningful),
        JsonValueKind.Object => value.EnumerateObject().Any(p => IsMeaningful(p.Value)),
        JsonValueKind.Number or JsonValueKind.True => true,
        _ => false
    };
}
