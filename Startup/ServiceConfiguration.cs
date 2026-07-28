namespace BooksMetadataBaker.Startup;

/// <summary>
/// Configures application services and dependency injection.
/// </summary>
public static class ServiceConfiguration
{
    /// <summary>
    /// Registers all application services.
    /// </summary>
    public static IServiceCollection ConfigureServices(this IServiceCollection services)
    {
        // API Services
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        // Caching
        services.AddMemoryCache();

        // Metadata sources (external API integrations)
        services.AddHttpClient<IMetadataSource, AniListService>(c => c.BaseAddress = new Uri("https://graphql.anilist.co"));
        services.AddHttpClient<IMetadataSource, GoogleBooksService>(c => c.BaseAddress = new Uri("https://www.googleapis.com/books/v1/volumes"));
        services.AddHttpClient<IMetadataSource, ComicVineService>(c => c.BaseAddress = new Uri("https://comicvine.gamespot.com/api"));

        // Application Services
        services.AddScoped<IAggregatedMetadataService, AggregatedMetadataService>();
        services.AddSingleton<IEBookMetadataUpdater, EBookMetadataUpdater>();
        services.AddSingleton<IKavitaMetadataWriter, KavitaMetadataWriter>();
        services.AddScoped<IUploadProcessingService, UploadProcessingService>();

        return services;
    }
}
