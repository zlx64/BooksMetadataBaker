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

        // Metadata sources (external API integrations)
        services.AddHttpClient<IMetadataSource, AniListService>(c =>
            c.BaseAddress = new Uri(GetBaseUrl(configuration, "PdfLibrary:AniList:BaseUrl", "https://graphql.anilist.co")));
        services.AddHttpClient<IMetadataSource, GoogleBooksService>(c =>
            c.BaseAddress = new Uri(GetBaseUrl(configuration, "PdfLibrary:GoogleBooks:BaseUrl", "https://www.googleapis.com/books/v1/volumes")));
        services.AddHttpClient<IMetadataSource, ComicVineService>(c =>
            c.BaseAddress = new Uri(GetBaseUrl(configuration, "PdfLibrary:ComicVine:BaseUrl", "https://comicvine.gamespot.com/api")));

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
