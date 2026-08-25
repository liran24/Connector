using System.Security.Cryptography;
using System.Text;

namespace AiVisibility.App.Shopify;

/// <summary>
/// Verifies that a request genuinely came from Shopify.
/// </summary>
/// <remarks>
/// Shopify signs two different things in two different ways, and mixing them up silently
/// fails every verification:
/// <list type="bullet">
/// <item>OAuth redirects carry a lowercase-hex HMAC over the sorted query string.</item>
/// <item>Webhooks carry a base64 HMAC over the raw, unparsed request body.</item>
/// </list>
/// Both comparisons are constant-time. A naive <c>==</c> leaks how much of the signature was
/// correct, which is enough to forge one given patience.
/// </remarks>
public sealed class ShopifySignature
{
    private readonly byte[] _secret;

    public ShopifySignature(string apiSecret) => _secret = Encoding.UTF8.GetBytes(apiSecret);

    /// <summary>
    /// Verifies the <c>hmac</c> parameter on an OAuth redirect or an embedded-app page load.
    /// </summary>
    /// <param name="query">
    /// Every query parameter from the request, including <c>hmac</c> itself, which is removed
    /// before the digest is computed.
    /// </param>
    public bool IsValidQuery(IEnumerable<KeyValuePair<string, string>> query)
    {
        var parameters = query.ToList();

        var supplied = parameters
            .FirstOrDefault(p => string.Equals(p.Key, "hmac", StringComparison.OrdinalIgnoreCase))
            .Value;

        if (string.IsNullOrEmpty(supplied))
        {
            return false;
        }

        // Shopify signs the parameters sorted by name, joined as key=value pairs, with the
        // signature itself excluded.
        var message = string.Join("&", parameters
            .Where(p => !string.Equals(p.Key, "hmac", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}"));

        var expected = Convert.ToHexString(ComputeDigest(Encoding.UTF8.GetBytes(message))).ToLowerInvariant();

        return FixedTimeEquals(expected, supplied);
    }

    /// <summary>
    /// Verifies the <c>X-Shopify-Hmac-Sha256</c> header on a webhook.
    /// </summary>
    /// <param name="rawBody">
    /// The body exactly as it arrived. Deserialising and re-serialising changes the bytes and
    /// breaks the digest, so the caller must buffer the raw request.
    /// </param>
    public bool IsValidWebhook(ReadOnlySpan<byte> rawBody, string? suppliedHeader)
    {
        if (string.IsNullOrEmpty(suppliedHeader))
        {
            return false;
        }

        var expected = Convert.ToBase64String(ComputeDigest(rawBody));
        return FixedTimeEquals(expected, suppliedHeader);
    }

    private byte[] ComputeDigest(ReadOnlySpan<byte> message)
    {
        using var hmac = new HMACSHA256(_secret);

        var digest = new byte[hmac.HashSize / 8];
        hmac.TryComputeHash(message, digest, out _);
        return digest;
    }

    /// <summary>
    /// Compares two signatures without leaking, through timing, how many characters matched.
    /// </summary>
    private static bool FixedTimeEquals(string expected, string supplied) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(supplied));
}
