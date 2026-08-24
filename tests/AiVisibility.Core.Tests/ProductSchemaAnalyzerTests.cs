using AiVisibility.Core.StructuredData;

namespace AiVisibility.Core.Tests;

public class ProductSchemaAnalyzerTests
{
    private static string PageWith(string jsonLd) =>
        $"<html><head><script type=\"application/ld+json\">{jsonLd}</script></head><body></body></html>";

    private const string CompleteProduct = """
        {
          "@context": "https://schema.org",
          "@type": "Product",
          "name": "Pour-over kettle",
          "description": "A gooseneck kettle for filter coffee.",
          "image": "https://shop.example/kettle.jpg",
          "brand": {"@type": "Brand", "name": "Example"},
          "sku": "KET-001",
          "color": "matte black",
          "aggregateRating": {"@type": "AggregateRating", "ratingValue": "4.6", "reviewCount": "88"},
          "offers": {
            "@type": "Offer",
            "price": "49.00",
            "priceCurrency": "USD",
            "availability": "https://schema.org/InStock",
            "shippingDetails": {"@type": "OfferShippingDetails"},
            "hasMerchantReturnPolicy": {"@type": "MerchantReturnPolicy"}
          }
        }
        """;

    [Fact]
    public void PageWithoutProductMarkup_ReturnsNull()
    {
        var missing = ProductSchemaAnalyzer.FindMissingFields("<html><body>Just a kettle</body></html>");

        Assert.Null(missing);
    }

    [Fact]
    public void CompleteProduct_HasNothingMissing()
    {
        var missing = ProductSchemaAnalyzer.FindMissingFields(PageWith(CompleteProduct));

        Assert.NotNull(missing);
        Assert.Empty(missing);
    }

    [Fact]
    public void MissingNestedOfferFields_AreReported()
    {
        var missing = ProductSchemaAnalyzer.FindMissingFields(PageWith("""
            {
              "@type": "Product",
              "name": "Kettle",
              "offers": {"@type": "Offer", "price": "49.00"}
            }
            """));

        Assert.NotNull(missing);
        Assert.Contains(missing, f => f.Path == "offers.availability");
        Assert.Contains(missing, f => f.Path == "offers.priceCurrency");
        Assert.DoesNotContain(missing, f => f.Path == "offers.price");
    }

    [Fact]
    public void AlternativeIdentifier_SatisfiesTheSkuRequirement()
    {
        var withGtin = ProductSchemaAnalyzer.FindMissingFields(PageWith("""
            {"@type": "Product", "name": "Kettle", "gtin13": "0123456789012"}
            """));

        Assert.NotNull(withGtin);
        Assert.DoesNotContain(withGtin, f => f.Path == "sku");
    }

    [Fact]
    public void EmptyValues_CountAsMissing()
    {
        var missing = ProductSchemaAnalyzer.FindMissingFields(PageWith("""
            {"@type": "Product", "name": "", "image": [], "description": "   "}
            """));

        Assert.NotNull(missing);
        Assert.Contains(missing, f => f.Path == "name");
        Assert.Contains(missing, f => f.Path == "image");
        Assert.Contains(missing, f => f.Path == "description");
    }

    [Fact]
    public void ProductInsideGraph_IsFound()
    {
        var missing = ProductSchemaAnalyzer.FindMissingFields(PageWith($$"""
            {"@context": "https://schema.org", "@graph": [{"@type": "WebSite"}, {{CompleteProduct}}]}
            """));

        Assert.NotNull(missing);
        Assert.Empty(missing);
    }

    [Fact]
    public void OfferArray_IsSatisfiedByAnyPricedOffer()
    {
        var missing = ProductSchemaAnalyzer.FindMissingFields(PageWith("""
            {
              "@type": "Product",
              "name": "Kettle",
              "offers": [
                {"@type": "Offer", "availability": "https://schema.org/OutOfStock"},
                {"@type": "Offer", "price": "49.00", "priceCurrency": "USD"}
              ]
            }
            """));

        Assert.NotNull(missing);
        Assert.DoesNotContain(missing, f => f.Path == "offers.price");
        Assert.DoesNotContain(missing, f => f.Path == "offers.availability");
    }

    [Fact]
    public void TypeArrayContainingProduct_IsRecognised()
    {
        var missing = ProductSchemaAnalyzer.FindMissingFields(PageWith("""
            {"@type": ["Product", "IndividualProduct"], "name": "Kettle"}
            """));

        Assert.NotNull(missing);
    }

    [Fact]
    public void MalformedJsonLd_DoesNotThrow()
    {
        var missing = ProductSchemaAnalyzer.FindMissingFields(PageWith("{ not json at all "));

        Assert.Null(missing);
    }

    [Fact]
    public void BrokenBlock_DoesNotHideAValidOne()
    {
        var html = "<html><head>"
            + "<script type=\"application/ld+json\">{ broken</script>"
            + $"<script type=\"application/ld+json\">{CompleteProduct}</script>"
            + "</head><body></body></html>";

        var missing = ProductSchemaAnalyzer.FindMissingFields(html);

        Assert.NotNull(missing);
        Assert.Empty(missing);
    }
}
