namespace BooksMetadataBaker.Startup;

/// <summary>
/// Configures HTTP clients for external service integrations.
/// </summary>
public static class HttpClientConfiguration
{
    /// <summary>
    /// Registers HTTP clients for external APIs.
    /// </summary>
    public static IServiceCollection ConfigureHttpClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<AniListService>(c =>
        {
            c.BaseAddress = new Uri(configuration["PdfLibrary:AniList:BaseUrl"]!);
        });

        services.AddHttpClient<GoogleBooksService>(c =>
        {
            c.BaseAddress = new Uri(configuration["PdfLibrary:GoogleBooks:BaseUrl"]!);
        });

        services.AddHttpClient<ComicVineService>(c =>
        {
            c.BaseAddress = new Uri(configuration["PdfLibrary:ComicVine:BaseUrl"]!);
        });
        
        return services;
    }
}
