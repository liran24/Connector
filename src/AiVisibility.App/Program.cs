using AiVisibility.App.Configuration;
using AiVisibility.App.Data;
using AiVisibility.App.Endpoints;
using AiVisibility.App.Scanning;
using AiVisibility.App.Security;
using AiVisibility.App.Shopify;
using AiVisibility.Core;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration
//
// Every section is validated at startup, so a missing secret stops the
// deployment naming the exact field instead of failing a merchant's install.
// ---------------------------------------------------------------------------
builder.Services.AddValidatedOptions<ShopifyOptions>(builder.Configuration, ShopifyOptions.SectionName);
builder.Services.AddValidatedOptions<BillingOptions>(builder.Configuration, BillingOptions.SectionName);
builder.Services.AddValidatedOptions<ScanScheduleOptions>(builder.Configuration, ScanScheduleOptions.SectionName);
builder.Services.AddValidatedOptions<TrackingOptions>(builder.Configuration, TrackingOptions.SectionName);

// ---------------------------------------------------------------------------
// Database
//
// SQLite locally, PostgreSQL in production. The provider is picked from the
// connection string so no query has to know which one it is talking to.
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
// Shopify
// ---------------------------------------------------------------------------
builder.Services.AddMemoryCache();

// This key ring encrypts stored access tokens. It must outlive the process and
// be shared by every replica — otherwise a restart makes stored tokens
// unreadable and every merchant has to reinstall.
builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        builder.Configuration["DataProtection:KeyRingPath"] ?? "./keyring"))
    .SetApplicationName("AiVisibility");

builder.Services.AddSingleton<IAccessTokenProtector, DataProtectionAccessTokenProtector>();
builder.Services.AddSingleton<IInstallStateStore, InstallStateStore>();
builder.Services.AddSingleton<ISessionTokenValidator, SessionTokenValidator>();

// One signature verifier for the whole app, built from the configured secret.
builder.Services.AddSingleton(provider =>
    new ShopifySignature(provider.GetRequiredService<IOptions<ShopifyOptions>>().Value.ApiSecret));

builder.Services.AddScoped<WebhookRequestReader>();
builder.Services.AddScoped<SessionTokenFilter>();
builder.Services.AddScoped<ActiveSubscriptionFilter>();
builder.Services.AddHttpClient<IAccessTokenExchange, AccessTokenExchange>();
builder.Services.AddHttpClient<ISubscriptionService, SubscriptionService>();

// ---------------------------------------------------------------------------
// Scanning
// ---------------------------------------------------------------------------
builder.Services.AddAiVisibilityScanner();
builder.Services.AddScoped<IShopScanService, ShopScanService>();
builder.Services.AddScoped<IShopTrackingService, ShopTrackingService>();
builder.Services.AddScoped<RecurringScanJob>();

var schedule = builder.Configuration
    .GetSection(ScanScheduleOptions.SectionName)
    .Get<ScanScheduleOptions>() ?? new ScanScheduleOptions();

if (schedule.Enabled)
{
    // In-memory storage keeps local development to a single process with nothing to install.
    // Swap for Hangfire.PostgreSql in production so a restart does not lose the schedule.
    builder.Services.AddHangfire(config => config.UseInMemoryStorage());
    builder.Services.AddHangfireServer(options => options.WorkerCount = schedule.WorkerCount);
}

// ---------------------------------------------------------------------------
// Dashboard
//
// The React app is built to wwwroot; serving it from here keeps the embedded
// app on the same origin as its API, which is what App Bridge expects.
// ---------------------------------------------------------------------------
var app = builder.Build();

// EnsureCreated keeps a single-instance deployment simple. Move to migrations run
// as a deploy step before running more than one replica, or they will race.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AiVisibilityDbContext>().Database.EnsureCreatedAsync();
}

if (schedule.Enabled)
{
    // Resolved from the container rather than Hangfire's static RecurringJob facade: the
    // static one reads a global JobStorage that a DI-configured app never sets, and fails
    // at startup.
    app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<RecurringScanJob>(
        RecurringScanJob.JobId,
        job => job.RunAsync(CancellationToken.None),
        schedule.Cron,
        new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapInstallEndpoints();
app.MapWebhookEndpoints();
app.MapDashboardApi();
app.MapBillingEndpoints();
app.MapTrackingEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Any unmatched path falls through to the dashboard so client-side routing works.
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed so the test project can start the app in-memory.</summary>
public partial class Program;

/// <summary>Binds and validates one options section.</summary>
internal static class OptionsRegistration
{
    public static IServiceCollection AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TOptions : class
    {
        services
            .AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
