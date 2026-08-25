using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace AiVisibility.App.Shopify;

/// <summary>
/// A verified <c>*.myshopify.com</c> domain.
/// </summary>
/// <remarks>
/// This type exists to make one class of bug impossible. Shopify passes the shop domain as a
/// query parameter, and an attacker can put anything there. An app that forwards that value
/// unchecked will happily send an access token, or an API call, to a host the attacker
/// controls. Parsing into this type is the only way to obtain a shop domain in this codebase,
/// so a raw string can never reach an outbound request.
/// </remarks>
public sealed partial class ShopDomain : IEquatable<ShopDomain>
{
    /// <summary>
    /// Deliberately strict: lowercase host, the literal <c>.myshopify.com</c> suffix, no port,
    /// no userinfo, no path. Anything looser reopens the hole this type closes.
    /// </summary>
    /// <remarks>
    /// Anchored with <c>\z</c> rather than <c>$</c>. In .NET <c>$</c> also matches immediately
    /// before a trailing newline, which would let "example.myshopify.com\n" through with the
    /// newline still in the value — a header-injection primitive once that value reaches an
    /// outbound request.
    /// </remarks>
    [GeneratedRegex(@"\A[a-z0-9][a-z0-9-]{0,59}\.myshopify\.com\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>The domain, e.g. <c>example.myshopify.com</c>.</summary>
    public string Value { get; }

    /// <summary>The storefront root, e.g. <c>https://example.myshopify.com/</c>.</summary>
    public Uri StorefrontUrl => new($"https://{Value}/");

    /// <summary>Base URL for Admin API calls against this shop.</summary>
    public Uri AdminApiUrl(string apiVersion) => new($"https://{Value}/admin/api/{apiVersion}/");

    private ShopDomain(string value) => Value = value;

    /// <summary>
    /// Parses a shop domain supplied by an untrusted caller.
    /// </summary>
    /// <param name="candidate">The raw <c>shop</c> parameter, which may be anything at all.</param>
    /// <returns>True when the value is a real Shopify shop domain.</returns>
    public static bool TryParse(string? candidate, [NotNullWhen(true)] out ShopDomain? shop)
    {
        shop = null;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalised = candidate.Trim().ToLowerInvariant();

        // Reject anything carrying a scheme, credentials, port or path rather than trying to
        // strip it — "https://evil.com@shop.myshopify.com" is exactly the input this guards against.
        if (!Pattern().IsMatch(normalised))
        {
            return false;
        }

        shop = new ShopDomain(normalised);
        return true;
    }

    /// <summary>Parses a value already known to be trusted, such as one read back from the database.</summary>
    /// <exception cref="ArgumentException">The stored value is not a valid shop domain.</exception>
    public static ShopDomain Parse(string candidate) =>
        TryParse(candidate, out var shop)
            ? shop
            : throw new ArgumentException($"Not a Shopify shop domain: '{candidate}'.", nameof(candidate));

    public bool Equals(ShopDomain? other) => other is not null && Value == other.Value;

    public override bool Equals(object? obj) => Equals(obj as ShopDomain);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;
}
