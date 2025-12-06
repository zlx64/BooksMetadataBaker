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

        // Application Services
        services.AddScoped<IAggregatedMetadataService, AggregatedMetadataService>();
        services.AddSingleton<IEBookMetadataUpdater, EBookMetadataUpdater>();
        services.AddScoped<IUploadProcessingService, UploadProcessingService>();
        
        return services;
    }
}
