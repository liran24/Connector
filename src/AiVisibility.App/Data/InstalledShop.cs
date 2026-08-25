using System.ComponentModel.DataAnnotations;

namespace AiVisibility.App.Data;

/// <summary>
/// Where a shop stands with its subscription.
/// </summary>
public enum SubscriptionStatus
{
    /// <summary>Installed, but has not yet been asked to pay.</summary>
    None = 0,

    /// <summary>A charge was created and the merchant has not yet approved it.</summary>
    PendingApproval = 1,

    /// <summary>Inside the free trial.</summary>
    Trialing = 2,

    /// <summary>Paying.</summary>
    Active = 3,

    /// <summary>Cancelled, declined, or frozen for non-payment.</summary>
    Inactive = 4
}

/// <summary>
/// One shop that has installed the app.
/// </summary>
/// <remarks>
/// The tenant row. Every piece of stored data hangs off this, so deleting it must delete
/// everything else — which is what <c>shop/redact</c> requires and what the cascade in
/// <see cref="AiVisibilityDbContext"/> enforces.
/// </remarks>
public sealed class InstalledShop
{
    public int Id { get; set; }

    /// <summary>The shop's <c>*.myshopify.com</c> domain. Unique — one row per shop.</summary>
    [MaxLength(80)]
    public required string Domain { get; set; }

    /// <summary>
    /// The Admin API access token, encrypted at rest.
    /// </summary>
    /// <remarks>
    /// Never assign a raw token here. <c>IAccessTokenProtector</c> owns the encryption, so a
    /// database dump or a stray log line does not hand over control of a merchant's store.
    /// </remarks>
    [MaxLength(1024)]
    public required string ProtectedAccessToken { get; set; }

    /// <summary>Scopes the merchant actually granted, which may differ from what was requested.</summary>
    [MaxLength(500)]
    public string GrantedScopes { get; set; } = string.Empty;

    public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.None;

    /// <summary>Shopify's id for the recurring charge, used to confirm approval later.</summary>
    public long? SubscriptionChargeId { get; set; }

    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set when the merchant uninstalls; the row is kept briefly for the redact webhook.</summary>
    public DateTimeOffset? UninstalledAt { get; set; }

    /// <summary>Whether the app should currently do paid work for this shop.</summary>
    public bool IsActive => UninstalledAt is null
        && SubscriptionStatus is SubscriptionStatus.Trialing or SubscriptionStatus.Active;

    public List<StoredScan> Scans { get; set; } = new();
}

/// <summary>
/// A completed scan, kept so the dashboard can show a merchant their score over time.
/// </summary>
public sealed class StoredScan
{
    public int Id { get; set; }

    public int InstalledShopId { get; set; }

    public InstalledShop? Shop { get; set; }

    public DateTimeOffset ScannedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The headline score, null when the store could not be read.</summary>
    public int? Score { get; set; }

    /// <summary>The full result as JSON, so a stored report survives changes to the scoring model.</summary>
    public required string ResultJson { get; set; }
}
