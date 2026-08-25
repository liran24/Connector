using System.ComponentModel.DataAnnotations;
using AiVisibility.App.Configuration;

namespace AiVisibility.App.Tests;

public class ConfigurationTests
{
    private static IReadOnlyList<string> Validate(object options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results.Select(r => r.ErrorMessage ?? string.Empty).ToList();
    }

    private static ShopifyOptions ValidShopify() => new()
    {
        ApiKey = "key",
        ApiSecret = "secret",
        AppUrl = "https://ai-visibility.example.com",
        Scopes = new[] { "read_products" }
    };

    [Fact]
    public void AFullyConfiguredAppPassesValidation()
    {
        Assert.Empty(Validate(ValidShopify()));
        Assert.Empty(Validate(new BillingOptions()));
    }

    [Fact]
    public void AMissingSecretIsReportedByName_SoTheFixIsObvious()
    {
        var options = ValidShopify();
        options.ApiSecret = string.Empty;

        Assert.Contains(Validate(options), message => message.Contains("Shopify:ApiSecret"));
    }

    [Fact]
    public void AnAppUrlThatIsNotAbsolute_IsRejected()
    {
        var options = ValidShopify();
        options.AppUrl = "ai-visibility.example.com";

        Assert.Contains(Validate(options), message => message.Contains("Shopify:AppUrl"));
    }

    [Fact]
    public void AnApiVersionInTheWrongShape_IsRejected()
    {
        var options = ValidShopify();
        options.ApiVersion = "latest";

        Assert.Contains(Validate(options), message => message.Contains("Shopify:ApiVersion"));
    }

    [Fact]
    public void RequestingNoScopes_IsRejected()
    {
        var options = ValidShopify();
        options.Scopes = Array.Empty<string>();

        Assert.Contains(Validate(options), message => message.Contains("Shopify:Scopes"));
    }

    [Fact]
    public void ScopesDefaultToEmpty_SoTheBinderCannotAppendToThem()
    {
        // Regression: a default value here was combined with the configured one by the
        // configuration binder, producing "read_products,read_products" on the authorize URL.
        Assert.Empty(new ShopifyOptions().Scopes);
    }

    [Fact]
    public void ScopesAreJoinedTheWayShopifyExpects()
    {
        var options = ValidShopify();
        options.Scopes = new[] { "read_products", "read_themes" };

        Assert.Equal("read_products,read_themes", options.ScopeParameter);
    }

    [Fact]
    public void TheCallbackUrlIsDerivedFromTheAppUrl()
    {
        Assert.Equal("https://ai-visibility.example.com/shopify/callback", ValidShopify().CallbackUrl.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ANonPositivePrice_IsRejected(decimal price)
    {
        var billing = new BillingOptions { MonthlyPrice = price };

        Assert.Contains(Validate(billing), message => message.Contains("Billing:MonthlyPrice"));
    }

    [Fact]
    public void ACurrencyThatIsNotAnIsoCode_IsRejected()
    {
        var billing = new BillingOptions { Currency = "dollars" };

        Assert.Contains(Validate(billing), message => message.Contains("Billing:Currency"));
    }

    [Fact]
    public void TestChargesAreOnByDefault_SoADevelopmentStoreCannotBeBilledForReal()
    {
        Assert.True(new BillingOptions().UseTestCharges);
    }
}
