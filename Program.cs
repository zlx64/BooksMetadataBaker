using Serilog;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Map short env vars to config keys (only if set)
    var envMap = new[]
    {
        ("ROOT_DIR", "PdfLibrary:RootFolder"),
        ("BOOK_DIR", "PdfLibrary:TypeFolders:Book"),
        ("LN_DIR", "PdfLibrary:TypeFolders:LightNovel"),
        ("MANGA_DIR", "PdfLibrary:TypeFolders:Manga"),
        ("COMIC_DIR", "PdfLibrary:TypeFolders:Comic"),
        ("GOOGLE_BOOKS_KEY", "PdfLibrary:GoogleBooks:ApiKey"),
        ("COMIC_VINE_KEY", "PdfLibrary:ComicVine:ApiKey"),
    };
    var overrides = envMap
        .Where(e => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(e.Item1)))
        .Select(e => new KeyValuePair<string, string?>(e.Item2, Environment.GetEnvironmentVariable(e.Item1)))
        .ToList();
    if (overrides.Count > 0)
        builder.Configuration.AddInMemoryCollection(overrides);

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
