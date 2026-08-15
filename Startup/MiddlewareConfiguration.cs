namespace BooksMetadataBaker.Startup;

/// <summary>
/// Configures the HTTP request pipeline.
/// </summary>
public static class MiddlewareConfiguration
{
    /// <summary>
    /// Configures the HTTP request pipeline.
    /// </summary>
    public static WebApplication ConfigureMiddleware(this WebApplication app)
    {
        // Outermost handler: log unhandled exceptions and never leak internals to clients
        app.Use(async (ctx, next) =>
        {
            try
            {
                await next();
            }
            catch (OperationCanceledException)
            {
                if (!ctx.Response.HasStarted)
                    ctx.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }
            catch (Exception ex)
            {
                ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("UnhandledException")
                    .LogError(ex, "Unhandled exception for {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
                if (!ctx.Response.HasStarted)
                {
                    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await ctx.Response.WriteAsJsonAsync(new { error = "Internal server error" });
                }
            }
        });

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // Only redirect when an HTTPS port is actually configured
        var httpsPort = app.Configuration["ASPNETCORE_HTTPS_PORTS"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_HTTPS_PORTS");
        if (!string.IsNullOrWhiteSpace(httpsPort))
            app.UseHttpsRedirection();

        // UI config probe (excluded from API-key auth; exposes no secrets)
        app.MapGet("/api/config", (IConfiguration cfg) =>
            Results.Ok(new { authRequired = !string.IsNullOrWhiteSpace(cfg["Auth:ApiKey"]) }));

        // Optional API key protection for /api endpoints (active only when Auth:ApiKey is set)
        var apiKey = app.Configuration["Auth:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            app.Use(async (ctx, next) =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api") && ctx.Request.Path != "/api/config")
                {
                    var provided = ctx.Request.Headers["X-Api-Key"].ToString();
                    var expectedBytes = Encoding.UTF8.GetBytes(apiKey);
                    var providedBytes = Encoding.UTF8.GetBytes(provided);
                    if (providedBytes.Length != expectedBytes.Length ||
                        !CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await ctx.Response.WriteAsync("Unauthorized");
                        return;
                    }
                }
                await next();
            });
        }

        app.UseRateLimiter();

        // Serve static UI
        app.UseStaticFiles();

        app.MapControllers();

        // Redirect root to UI page
        app.MapGet("/", () => Results.Redirect("/index.html"));

        return app;
    }
}
