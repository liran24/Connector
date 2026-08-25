using System.ComponentModel.DataAnnotations;

namespace AiVisibility.App.Configuration;

/// <summary>
/// Visibility tracking: asking assistants shopper questions and seeing who they recommend.
/// </summary>
/// <remarks>
/// This is the only part of the app with a per-run cost, so its settings are the ones that
/// decide what the subscription costs to serve. Each category becomes three API calls per
/// run, per engine.
/// </remarks>
public sealed class TrackingOptions
{
    /// <summary>Configuration section name: <c>Tracking</c>.</summary>
    public const string SectionName = "Tracking";

    /// <summary>
    /// Whether tracking is available at all. Off by default: without an API key the feature
    /// cannot work, and a merchant seeing a button that always fails is worse than not
    /// seeing it.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Anthropic API key. Like the Shopify secret, never in appsettings.json — user-secrets
    /// locally, an environment variable in production.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// How many product categories each run covers. Three API calls per category, so this is
    /// the direct lever on cost per shop per run.
    /// </summary>
    [Range(1, 10, ErrorMessage = "Tracking:CategoriesPerRun must be between 1 and 10.")]
    public int CategoriesPerRun { get; set; } = 2;

    /// <summary>Which Claude model answers the shopper questions.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Model { get; set; } = "claude-opus-5";

    /// <summary>Whether the configuration is complete enough to actually run.</summary>
    public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(ApiKey);
}
