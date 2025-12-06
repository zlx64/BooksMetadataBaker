namespace BooksMetadataBaker.Startup;

/// <summary>
/// Configures middleware pipeline for the application.
/// </summary>
public static class MiddlewareConfiguration
{
    /// <summary>
    /// Configures the HTTP request pipeline.
    /// </summary>
    public static WebApplication ConfigureMiddleware(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseAuthorization();

        // Serve static UI
        app.UseStaticFiles();

        app.MapControllers();

        // Redirect root to UI page
        app.MapGet("/", () => Results.Redirect("/index.html"));
        
        return app;
    }
}
