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
        // The Serilog section in appsettings.json already defines a Console (and
        // File) sink. Only add a fallback Console sink when none are configured,
        // otherwise every log line is written twice.
        var hasConfiguredSinks =
            builder.Configuration.GetSection("Serilog:WriteTo").GetChildren().Any();

        var config = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId();

        if (!hasConfiguredSinks)
            config = config.WriteTo.Console();

        Log.Logger = config.CreateLogger();

        builder.Host.UseSerilog();

        return builder;
    }
}
