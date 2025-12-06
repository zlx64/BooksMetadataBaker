using Serilog;

namespace BooksMetadataBaker.Startup;

/// <summary>
/// Configures Serilog logging for the application.
/// </summary>
public static class LoggingConfiguration
{
    /// <summary>
    /// Configures Serilog as the logging provider.
    /// </summary>
    public static WebApplicationBuilder ConfigureSerilog(this WebApplicationBuilder builder)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .WriteTo.Console() // fallback if config missing
            .CreateLogger();

        builder.Host.UseSerilog();
        
        return builder;
    }
}
