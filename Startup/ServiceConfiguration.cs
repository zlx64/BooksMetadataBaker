using System.Globalization;
using Microsoft.AspNetCore.RateLimiting;

namespace BooksMetadataBaker.Startup;

/// <summary>
/// Configures application services and dependency injection.
/// </summary>
public static class ServiceConfiguration
{
    /// <summary>
    /// Registers all application services.
    /// </summary>
    public static IServiceCollection ConfigureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // API Services
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        // Caching
        services.AddMemoryCache();

        // Metadata sources (external API integrations).
        // Each service must be its own named typed client: AddHttpClient<TClient, TImpl>
        // keys the HttpClient config by TClient, so registering all three under
        // IMetadataSource would make the last BaseAddress win for every source.
        services.AddHttpClient<AniListService>(c =>
            c.BaseAddress = new Uri(GetBaseUrl(configuration, "PdfLibrary:AniList:BaseUrl", "https://graphql.anilist.co")));
        services.AddHttpClient<GoogleBooksService>(c =>
            c.BaseAddress = new Uri(GetBaseUrl(configuration, "PdfLibrary:GoogleBooks:BaseUrl", "https://www.googleapis.com/books/v1/volumes")));
        services.AddHttpClient<ComicVineService>(c =>
            c.BaseAddress = new Uri(GetBaseUrl(configuration, "PdfLibrary:ComicVine:BaseUrl", "https://comicvine.gamespot.com/api")));
        services.AddScoped<IMetadataSource>(sp => sp.GetRequiredService<AniListService>());
        services.AddScoped<IMetadataSource>(sp => sp.GetRequiredService<GoogleBooksService>());
        services.AddScoped<IMetadataSource>(sp => sp.GetRequiredService<ComicVineService>());

        // Rate limiting
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("upload", o =>
            {
                o.PermitLimit = int.Parse(configuration["RateLimiting:UploadPermitLimit"] ?? "10", NumberStyles.Integer, CultureInfo.InvariantCulture);
                o.Window = TimeSpan.FromSeconds(int.Parse(configuration["RateLimiting:UploadWindowSeconds"] ?? "60", NumberStyles.Integer, CultureInfo.InvariantCulture));
                o.QueueLimit = 0;
            });
        });

        // Application Services
        services.AddScoped<IAggregatedMetadataService, AggregatedMetadataService>();
        services.AddSingleton<IEBookMetadataUpdater, EBookMetadataUpdater>();
        services.AddSingleton<IKavitaMetadataWriter, KavitaMetadataWriter>();
        services.AddScoped<IUploadProcessingService, UploadProcessingService>();

        return services;

        static string GetBaseUrl(IConfiguration config, string key, string fallback)
        {
            var value = config[key];
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}
