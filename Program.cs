using PrepKavitaPdf.Services;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSingleton<IPdfMetadataUpdater, PdfMetadataUpdater>();

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

app.Run();
