using AiVisibility.App.Data;

namespace AiVisibility.App.Shopify;

/// <summary>
/// A webhook that has been read and checked.
/// </summary>
/// <param name="IsVerified">
/// Whether the signature matched and the shop domain parsed. False means the request must be
/// rejected — every other field is then untrustworthy.
/// </param>
/// <param name="Shop">The shop the webhook is about; non-null whenever verified.</param>
/// <param name="Body">The raw body, for handlers that need the payload.</param>
public sealed record VerifiedWebhook(bool IsVerified, ShopDomain? Shop, byte[] Body);

/// <summary>
/// Buffers a webhook request and verifies it before any handler looks at it.
/// </summary>
/// <remarks>
/// Shared by every webhook endpoint so no handler can accidentally skip verification. It also
/// carries the database and logger the handlers need, which keeps each endpoint to a single
/// injected parameter.
/// </remarks>
public sealed class WebhookRequestReader
{
    private const string SignatureHeader = "X-Shopify-Hmac-Sha256";
    private const string ShopHeader = "X-Shopify-Shop-Domain";

    /// <summary>Webhook bodies are small; anything larger is not from Shopify.</summary>
    private const int MaxBodyBytes = 1024 * 1024;

    private readonly ShopifySignature _signature;

    public AiVisibilityDbContext Database { get; }

    public ILogger<WebhookRequestReader> Logger { get; }

    public WebhookRequestReader(
        ShopifySignature signature,
        AiVisibilityDbContext database,
        ILogger<WebhookRequestReader> logger)
    {
        _signature = signature;
        Database = database;
        Logger = logger;
    }

    public async Task<VerifiedWebhook> ReadAsync(HttpContext context)
    {
        var body = await ReadBodyAsync(context).ConfigureAwait(false);

        var signatureValid = _signature.IsValidWebhook(body, context.Request.Headers[SignatureHeader]);
        if (!signatureValid)
        {
            Logger.LogWarning(
                "Rejected webhook on {Path}: signature did not match.",
                context.Request.Path);

            return new VerifiedWebhook(false, null, body);
        }

        if (!ShopDomain.TryParse(context.Request.Headers[ShopHeader], out var shop))
        {
            Logger.LogWarning("Rejected webhook on {Path}: missing or invalid shop header.", context.Request.Path);
            return new VerifiedWebhook(false, null, body);
        }

        return new VerifiedWebhook(true, shop, body);
    }

    /// <summary>
    /// Reads the body as raw bytes. The HMAC covers exactly these bytes, so deserialising
    /// first — or letting the framework re-encode them — breaks verification.
    /// </summary>
    private static async Task<byte[]> ReadBodyAsync(HttpContext context)
    {
        context.Request.EnableBuffering();

        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);

        context.Request.Body.Position = 0;

        return buffer.Length > MaxBodyBytes
            ? Array.Empty<byte>()
            : buffer.ToArray();
    }
}
