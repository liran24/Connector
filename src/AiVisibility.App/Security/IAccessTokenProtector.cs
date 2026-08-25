using Microsoft.AspNetCore.DataProtection;

namespace AiVisibility.App.Security;

/// <summary>
/// Encrypts Shopify access tokens before they are stored, and decrypts them for use.
/// </summary>
/// <remarks>
/// A Shopify access token is full API control of a merchant's store. Storing it in plaintext
/// means a leaked database backup is a leaked set of storefronts, so it is encrypted at rest
/// and only ever decrypted at the moment of an API call.
/// </remarks>
public interface IAccessTokenProtector
{
    string Protect(string accessToken);

    /// <summary>
    /// Decrypts a stored token.
    /// </summary>
    /// <returns>
    /// The token, or null when it cannot be decrypted — which happens if the data protection
    /// keys were lost. That shop must reinstall; treat it as an expected condition, not a crash.
    /// </returns>
    string? Unprotect(string protectedToken);
}

/// <summary>
/// Encrypts tokens with ASP.NET Core Data Protection.
/// </summary>
/// <remarks>
/// The key ring must be persisted and shared across instances. On the default in-memory
/// provider, a restart or a second replica loses the keys and every stored token becomes
/// unreadable — see the key ring configuration in <c>Program.cs</c>.
/// </remarks>
public sealed class DataProtectionAccessTokenProtector : IAccessTokenProtector
{
    /// <summary>Namespacing the purpose keeps these tokens un-decryptable by any other feature.</summary>
    private const string Purpose = "AiVisibility.ShopifyAccessToken.v1";

    private readonly IDataProtector _protector;

    public DataProtectionAccessTokenProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector(Purpose);

    public string Protect(string accessToken) => _protector.Protect(accessToken);

    public string? Unprotect(string protectedToken)
    {
        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
