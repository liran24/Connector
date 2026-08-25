using AiVisibility.Core.Catalog;

namespace AiVisibility.Core.Tracking;

/// <summary>
/// Turns a store's own catalogue into the questions its shoppers would actually ask an
/// assistant.
/// </summary>
/// <remarks>
/// Deliberately generates <em>unbranded</em> questions. Asking an assistant about a store by
/// name proves nothing — it will find it. The commercially meaningful question is whether the
/// store surfaces when a shopper describes a need and names no brand, because that is the
/// query a new customer types.
/// </remarks>
public static class PromptGenerator
{
    /// <summary>
    /// Builds a prompt set from the categories a store actually sells in.
    /// </summary>
    /// <param name="products">Catalogue sample; product types drive the prompts.</param>
    /// <param name="maxCategories">
    /// How many categories to cover. Each costs an API call per engine per run, so this is
    /// the main lever on what tracking costs to operate.
    /// </param>
    public static IReadOnlyList<ShopperPrompt> ForCatalogue(
        IReadOnlyList<CatalogProduct> products,
        int maxCategories = 3)
    {
        var categories = products
            .Select(p => p.ProductType)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type!.Trim())
            .GroupBy(type => type, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(maxCategories)
            .Select(g => g.Key)
            .ToList();

        // A store with no product_type set still deserves tracking, so fall back to product
        // titles rather than returning nothing.
        if (categories.Count == 0)
        {
            categories = products
                .Select(p => p.Title)
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Take(maxCategories)
                .ToList();
        }

        return categories.SelectMany(ForCategory).ToList();
    }

    /// <summary>
    /// The three shapes of question that decide whether a store gets discovered: the
    /// recommendation request, the purchase-intent request, and the comparison.
    /// </summary>
    public static IReadOnlyList<ShopperPrompt> ForCategory(string category)
    {
        var subject = category.Trim().ToLowerInvariant();

        return new[]
        {
            new ShopperPrompt($"What are the best {subject} to buy online right now?", category),
            new ShopperPrompt($"Where can I buy good quality {subject}?", category),
            new ShopperPrompt($"Which online shops sell the best {subject}, and how do they compare?", category)
        };
    }
}
