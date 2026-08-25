using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace AiVisibility.App.Shopify;

/// <summary>
/// Issues and redeems the one-time <c>state</c> nonce that ties an install redirect to the
/// callback that follows it.
/// </summary>
/// <remarks>
/// Without this, anyone can invoke the callback URL with a code of their choosing. The nonce
/// is single-use and bound to the shop, so a callback for a different shop, or a replay of an
/// old one, is rejected.
/// </remarks>
public interface IInstallStateStore
{
    /// <summary>Creates a nonce for an install that is starting.</summary>
    string Issue(ShopDomain shop);

    /// <summary>
    /// Consumes a nonce returned by Shopify.
    /// </summary>
    /// <returns>True when the nonce was issued for this shop and has not been used before.</returns>
    bool TryRedeem(ShopDomain shop, string? state);
}

/// <summary>
/// Holds pending install nonces in memory.
/// </summary>
/// <remarks>
/// In-memory is deliberate and adequate: a nonce lives for the seconds between redirect and
/// callback. Running several replicas behind a load balancer needs either sticky sessions or
/// a distributed cache, since a callback landing on a different instance would find no nonce.
/// </remarks>
public sealed class InstallStateStore : IInstallStateStore
{
    /// <summary>Long enough for a merchant to read the permission screen, short enough to limit replay.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly IMemoryCache _cache;

    public InstallStateStore(IMemoryCache cache) => _cache = cache;

    public string Issue(ShopDomain shop)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _cache.Set(KeyFor(shop, nonce), true, Lifetime);
        return nonce;
    }

    public bool TryRedeem(ShopDomain shop, string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        var key = KeyFor(shop, state);
        if (!_cache.TryGetValue(key, out _))
        {
            return false;
        }

        // Single use: remove it so the same callback cannot be replayed.
        _cache.Remove(key);
        return true;
    }

    private static string KeyFor(ShopDomain shop, string nonce) => $"install-state:{shop.Value}:{nonce}";
}
