using AiVisibility.App.Configuration;
using AiVisibility.App.Data;
using AiVisibility.App.Endpoints;
using AiVisibility.App.Security;
using AiVisibility.App.Shopify;
using AiVisibility.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration. Both sections are validated at startup, so a missing secret
// stops the deployment with a named field instead of failing a merchant's install.
// ---------------------------------------------------------------------------
builder.Services
    .AddOptions<ShopifyOptions>()
    .Bind(builder.Configuration.GetSection(ShopifyOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<BillingOptions>()
    .Bind(builder.Configuration.GetSection(BillingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ---------------------------------------------------------------------------
// Database. SQLite locally, PostgreSQL in production — chosen by connection
// string so no query has to know which one it is talking to.
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? "Data Source=aivisibility.db";

builder.Services.AddDbContext<AiVisibilityDbContext>(options =>
{
    if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

// ---------------------------------------------------------------------------
// Shopify plumbing.
// ---------------------------------------------------------------------------
builder.Services.AddMemoryCache();

// The key ring encrypts stored access tokens. It must outlive the process and be
// shared across replicas — otherwise a restart makes every stored token unreadable
// and every merchant has to reinstall.
builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        builder.Configuration["DataProtection:KeyRingPath"] ?? "./keyring"))
    .SetApplicationName("AiVisibility");

builder.Services.AddSingleton<IAccessTokenProtector, DataProtectionAccessTokenProtector>();
builder.Services.AddSingleton<IInstallStateStore, InstallStateStore>();

// One signature verifier for the whole app, built from the configured secret.
builder.Services.AddSingleton(provider =>
    new ShopifySignature(provider.GetRequiredService<IOptions<ShopifyOptions>>().Value.ApiSecret));

builder.Services.AddScoped<WebhookRequestReader>();
builder.Services.AddHttpClient<IAccessTokenExchange, AccessTokenExchange>();
builder.Services.AddHttpClient<ISubscriptionService, SubscriptionService>();

// The scanning engine, registered by contract so its checks can be extended here.
builder.Services.AddAiVisibilityScanner();

var app = builder.Build();

// Applying migrations at startup keeps a single-instance deployment simple. Move this
// to a deploy step before running more than one replica, or they will race.
await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<AiVisibilityDbContext>();
    await database.Database.EnsureCreatedAsync();
}

app.MapInstallEndpoints();
app.MapWebhookEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>Exposed so the test project can spin the app up in-memory.</summary>
public partial class Program;
