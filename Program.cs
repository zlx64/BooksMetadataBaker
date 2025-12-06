using PrepKavitaPdf.Services;
using PrepKavitaPdf.Services.Abstract;
using PrepKavitaPdf.Services.Integration;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog early so host logging uses it.
// Reads configuration from appsettings (Logging and Serilog sections)
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithProcessId()
    .Enrich.WithThreadId()
    .WriteTo.Console() // fallback if config missing
    .CreateLogger();

builder.Host.UseSerilog();

// Services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Caching
builder.Services.AddMemoryCache();

// HttpClients
builder.Services.AddHttpClient<AniListService>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["PdfLibrary:AniList:BaseUrl"]!);
});
builder.Services.AddHttpClient<GoogleBooksService>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["PdfLibrary:GoogleBooks:BaseUrl"]!);
});
builder.Services.AddHttpClient<ComicVineService>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["PdfLibrary:ComicVine:BaseUrl"]!);
});

builder.Services.AddScoped<IAggregatedMetadataService, AggregatedMetadataService>();
builder.Services.AddSingleton<IEBookMetadataUpdater, EBookMetadataUpdater>();
// Upload processing service
builder.Services.AddScoped<IUploadProcessingService, UploadProcessingService>();

var app = builder.Build();

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

try
{
    Log.Information("Starting PrepKavitaPdf web application - now supports PDF and EPUB files");
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
