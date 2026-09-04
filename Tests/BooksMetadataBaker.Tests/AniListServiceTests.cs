using System.Net;
using System.Net.Http;
using System.Text;
using BooksMetadataBaker.Models;
using BooksMetadataBaker.Services.Integration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BooksMetadataBaker.Tests;

public class AniListServiceTests
{
    // Captures the outgoing request body so tests can assert on the GraphQL
    // variables actually sent (the format:null bug is a request-shape bug).
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string json;
        public string? LastRequestBody { get; private set; }
        public CapturingHandler(string json) => this.json = json;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private static (AniListService svc, CapturingHandler handler) Create(string json)
    {
        var handler = new CapturingHandler(json);
        var svc = new AniListService(
            new HttpClient(handler) { BaseAddress = new Uri("https://graphql.anilist.co/") },
            NullLogger<AniListService>.Instance);
        return (svc, handler);
    }

    private const string NoMatchJson = """{"data":{"Media":null}}""";

    [Fact]
    public async Task MangaType_FormatVariableOmittedFromBody()
    {
        // Regression: sending "format":null makes AniList return HTTP 404 "Not Found".
        // For manga the format variable must be absent from the request body.
        var (svc, handler) = Create(NoMatchJson);

        await svc.TryFetchAsync("Koiseyo Mayakashi Tenshi-domo", BookType.Manga, CancellationToken.None);

        Assert.NotNull(handler.LastRequestBody);
        Assert.DoesNotContain("\"format\":null", handler.LastRequestBody);
        Assert.Contains("\"search\":\"Koiseyo Mayakashi Tenshi-domo\"", handler.LastRequestBody);
        Assert.Contains("\"type\":\"MANGA\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task LightNovelType_FormatVariableIncluded()
    {
        var (svc, handler) = Create(NoMatchJson);

        await svc.TryFetchAsync("Some Light Novel", BookType.LightNovel, CancellationToken.None);

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"format\":\"NOVEL\"", handler.LastRequestBody);
        Assert.Contains("\"type\":\"MANGA\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task StaffMapping_PrefersCreatorsOverLocalization()
    {
        var (svc, _) = Create("""
            {"data":{"Media":{"id":164263,
              "title":{"romaji":"Koiseyo Mayakashi Tenshi-domo","english":"Fall in Love, You False Angels","native":"恋せよまやかし天使ども"},
              "description":"A sweet romance.","siteUrl":"https://anilist.co/manga/164263","format":"MANGA","status":"RELEASING",
              "averageScore":79,"volumes":null,"chapters":null,"genres":["Comedy","Romance"],
              "startDate":{"year":2023,"month":4,"day":24},"endDate":null,
              "staff":{"edges":[
                {"role":"Story & Art","node":{"name":{"full":"Coco Uzuki"}}},
                {"role":"Translator (Chinese)","node":{"name":{"full":"Yihua Zeng"}}},
                {"role":"Editing (Portuguese)","node":{"name":{"full":"Patricia Machado"}}}]}}}}
            """);

        var meta = await svc.TryFetchAsync("Koiseyo Mayakashi Tenshi-domo", BookType.Manga, CancellationToken.None);

        Assert.Equal("Coco Uzuki", meta["Authors"]);
        Assert.Equal("Fall in Love, You False Angels", meta["TitleEnglish"]);
        Assert.Equal("A sweet romance.", meta["Description"]);
        Assert.Equal("Comedy, Romance", meta["Genres"]);
        Assert.Equal("2023-04-24", meta["StartDate"]);
        Assert.Equal("AniList", meta["Source"]);
    }

    [Fact]
    public async Task StaffMapping_FallsBackToAllWhenNoCreatorRole()
    {
        var (svc, _) = Create("""
            {"data":{"Media":{"id":1,"title":{"romaji":"S","english":null,"native":null},
              "description":null,"siteUrl":null,"format":"MANGA","status":"FINISHED",
              "averageScore":null,"volumes":null,"chapters":null,"genres":null,
              "startDate":null,"endDate":null,
              "staff":{"edges":[
                {"role":"Translator","node":{"name":{"full":"Trans One"}}},
                {"role":"Editing","node":{"name":{"full":"Edit Two"}}}]}}}}
            """);

        var meta = await svc.TryFetchAsync("S", BookType.Manga, CancellationToken.None);

        Assert.Equal("Trans One, Edit Two", meta["Authors"]);
    }

    [Fact]
    public async Task NoMatch_ReturnsEmpty()
    {
        var (svc, _) = Create(NoMatchJson);

        var meta = await svc.TryFetchAsync("Unknown Title", BookType.Manga, CancellationToken.None);

        Assert.Empty(meta);
    }

    [Fact]
    public async Task NonMangaType_ReturnsEmpty()
    {
        var (svc, handler) = Create(NoMatchJson);

        var meta = await svc.TryFetchAsync("Some Book", BookType.Book, CancellationToken.None);

        Assert.Empty(meta);
        // No HTTP call should be made for non-manga/light-novel types.
        Assert.Null(handler.LastRequestBody);
    }
}
