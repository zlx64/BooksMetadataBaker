using Serilog;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.ConfigureSerilog();
    builder.Services
        .ConfigureServices()
        .ConfigureHttpClients(builder.Configuration);

    var app = builder.Build();
    app.ConfigureMiddleware();
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
