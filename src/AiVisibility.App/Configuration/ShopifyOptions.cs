using System.ComponentModel.DataAnnotations;

namespace AiVisibility.App.Configuration;

/// <summary>
/// Everything the app needs to identify itself to Shopify.
/// </summary>
/// <remarks>
/// Validated when the application starts, not when a merchant first installs. A missing
/// secret should stop a deployment immediately with a named field, rather than surface hours
/// later as a failed install.
/// </remarks>
public sealed class ShopifyOptions
{
    /// <summary>Configuration section name: <c>Shopify</c>.</summary>
    public const string SectionName = "Shopify";

    /// <summary>
    /// The app's API key, shown as "Client ID" in the Shopify Partner dashboard.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Shopify:ApiKey is required (Partner dashboard → Client ID).")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The app's secret, shown as "Client secret". Signs every request Shopify sends, so a
    /// wrong value makes every install and webhook fail verification.
    /// </summary>
    /// <remarks>Never put this in appsettings.json — use user-secrets locally, env vars in production.</remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Shopify:ApiSecret is required (Partner dashboard → Client secret).")]
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// Public HTTPS base URL of this app, e.g. https://ai-visibility.example.com. Must match
    /// the App URL configured in the Partner dashboard, or Shopify rejects the redirect.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Shopify:AppUrl is required and must match the Partner dashboard App URL.")]
    [Url(ErrorMessage = "Shopify:AppUrl must be an absolute URL, e.g. https://ai-visibility.example.com.")]
    public string AppUrl { get; set; } = string.Empty;

    /// <summary>
    /// Access scopes requested at install. Keep this minimal: every extra scope is another
    /// permission screen a merchant can decline, and Shopify's review asks you to justify each.
    /// </summary>
    /// <remarks>
    /// Starts empty on purpose. The configuration binder <em>appends</em> configured entries to
    /// whatever the property already holds, so a default value here would be silently combined
    /// with the configured one — which produced "read_products,read_products" on the authorize
    /// URL. Validation below requires the value to come from configuration instead.
    /// </remarks>
    [MinLength(1, ErrorMessage = "Shopify:Scopes must request at least one scope.")]
    public string[] Scopes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Admin API version to call, as <c>YYYY-MM</c>. Shopify supports each version for a year,
    /// so this is pinned deliberately rather than tracking the latest.
    /// </summary>
    [RegularExpression(@"^\d{4}-\d{2}$", ErrorMessage = "Shopify:ApiVersion must look like 2025-07.")]
    public string ApiVersion { get; set; } = "2025-07";

    /// <summary>The scope string as Shopify expects it on the authorize URL.</summary>
    public string ScopeParameter => string.Join(",", Scopes);

    /// <summary>Where Shopify sends the merchant back after they approve the install.</summary>
    public Uri CallbackUrl => new(new Uri(AppUrl), "/shopify/callback");
}
