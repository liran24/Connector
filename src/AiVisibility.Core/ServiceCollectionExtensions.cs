using AiVisibility.Core.Checks;
using AiVisibility.Core.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AiVisibility.Core;

/// <summary>
/// Registers the scanner with a dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the storefront scanner and its four checks.
    /// </summary>
    /// <remarks>
    /// Checks are registered as a set rather than by name, so the host application can add its
    /// own <see cref="ICheck"/> and have the scanner pick it up without any change here.
    /// The fetcher goes through <c>IHttpClientFactory</c> so a long-running host reuses
    /// connections instead of exhausting sockets.
    /// </remarks>
    public static IServiceCollection AddAiVisibilityScanner(this IServiceCollection services)
    {
        services.AddHttpClient<IPageFetcher, HttpPageFetcher>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        });

        services.AddSingleton<AreaWeights>();
        services.AddScoped<IStorefrontProbe, StorefrontProbe>();

        services.AddScoped<ICheck, CrawlerAccessCheck>();
        services.AddScoped<ICheck, StructuredDataCheck>();
        services.AddScoped<ICheck, ContentDepthCheck>();
        services.AddScoped<ICheck, DiscoveryFilesCheck>();

        services.AddScoped<StoreScanner>();

        return services;
    }
}
