using System.Net;
using System.Net.Http;
using System.Text;
using BooksMetadataBaker.Models;
using BooksMetadataBaker.Services.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BooksMetadataBaker.Tests;

public class ComicVineServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string json;
        public StubHandler(string json) => this.json = json;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(resp);
        }
    }

    private static ComicVineService Create(string json) =>
        new(
            new HttpClient(new StubHandler(json))
            {
                BaseAddress = new Uri("https://comicvine.gamespot.com/api/")
            },
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["PdfLibrary:ComicVine:ApiKey"] = "test-key" })
                .Build(),
            NullLogger<ComicVineService>.Instance);

    [Fact]
    public async Task VolumeSearch_MapsSeriesLevelFields()
    {
        var svc = Create("""
            {"results":[{"name":"Test Series","description":"<p>Desc</p>","site_detail_url":"https://cv/test",
              "start_year":"2016","count_of_issues":"12","publisher":{"name":"Acme"},"api_detail_url":"https://cv/api/1"}]}
            """);

        var meta = await svc.TryFetchAsync("Test Series", BookType.Comic, CancellationToken.None);

        Assert.Equal("Test Series", meta["Title"]);
        Assert.Equal("Desc", meta["Description"]);
        Assert.Equal("https://cv/test", meta["SourceUrl"]);
        Assert.Equal("2016", meta["StartYear"]);
        Assert.Equal("12", meta["IssueCount"]);
        Assert.Equal("Acme", meta["Publisher"]);
        Assert.Equal("ComicVine", meta["Source"]);
        Assert.False(meta.ContainsKey("IssueNumber"), "multi-issue volume must not guess the issue");
        Assert.False(meta.ContainsKey("IssueName"));
    }

    [Fact]
    public async Task SingleIssueVolume_MapsIssueNumberAndName()
    {
        var svc = Create("""
            {"results":[{"name":"One Shot","count_of_issues":"1",
              "first_issue":{"id":5,"issue_number":"1","name":"One Shot #1"},
              "last_issue":{"id":5,"issue_number":"1","name":"One Shot #1"}}]}
            """);

        var meta = await svc.TryFetchAsync("One Shot", BookType.Comic, CancellationToken.None);

        Assert.Equal("1", meta["IssueNumber"]);
        Assert.Equal("One Shot #1", meta["IssueName"]);
    }

    [Fact]
    public async Task MultiIssueVolume_SameFirstLastId_MapsSingleIssue()
    {
        // count_of_issues absent, but first/last issue ids agree -> single issue.
        var svc = Create("""
            {"results":[{"name":"Solo","first_issue":{"id":9,"issue_number":"3","name":"Solo #3"},
              "last_issue":{"id":9,"issue_number":"3","name":"Solo #3"}}]}
            """);

        var meta = await svc.TryFetchAsync("Solo", BookType.Comic, CancellationToken.None);

        Assert.Equal("3", meta["IssueNumber"]);
        Assert.Equal("Solo #3", meta["IssueName"]);
    }

    [Fact]
    public async Task PersonCredits_MapToStaffKeysByRole_FirstPerRoleWins()
    {
        var svc = Create("""
            {"results":[{"name":"S","person_credits":[
              {"name":"Jane Doe","role":"writer, penciller"},
              {"name":"John Roe","role":"colorist"},
              {"name":"Late Writer","role":"writer"}]}]}
            """);

        var meta = await svc.TryFetchAsync("S", BookType.Comic, CancellationToken.None);

        Assert.Equal("Jane Doe", meta["StaffWriter"]);
        Assert.Equal("Jane Doe", meta["StaffPenciller"]);
        Assert.Equal("John Roe", meta["StaffColorist"]);
        Assert.False(meta.ContainsKey("StaffInker"));
        Assert.False(meta.ContainsKey("StaffEditor"));
    }

    [Fact]
    public async Task OptionalFields_AreMappedAndLanguageNormalized()
    {
        var svc = Create("""
            {"results":[{"name":"S","deck":"Short synopsis","language":"English","rating":"T+",
              "format":"Annual","isbn":"978-1-23","page_count":"32","start_date":"2020-05-01",
              "imprint":{"name":"Imp"}}]}
            """);

        var meta = await svc.TryFetchAsync("S", BookType.Comic, CancellationToken.None);

        Assert.Equal("Short synopsis", meta["Description"]);
        Assert.Equal("en", meta["LanguageISO"]);
        Assert.Equal("T+", meta["Rating"]);
        Assert.Equal("Annual", meta["ComicFormat"]);
        Assert.Equal("978-1-23", meta["Isbn"]);
        Assert.Equal("32", meta["PageCount"]);
        Assert.Equal("2020-05-01", meta["StartDate"]);
        Assert.Equal("Imp", meta["Imprint"]);
    }

    [Fact]
    public async Task UnrecognizedLanguage_IsNotEmitted()
    {
        var svc = Create("""{"results":[{"name":"S","language":"Esperanto"}]}""");

        var meta = await svc.TryFetchAsync("S", BookType.Comic, CancellationToken.None);

        Assert.False(meta.ContainsKey("LanguageISO"));
    }

    [Fact]
    public async Task NonComicType_ReturnsEmpty()
    {
        var svc = Create("""{"results":[{"name":"S"}]}""");

        var meta = await svc.TryFetchAsync("S", BookType.Manga, CancellationToken.None);

        Assert.Empty(meta);
    }

    [Fact]
    public async Task NoResults_ReturnsEmpty()
    {
        var svc = Create("""{"results":[]}""");

        var meta = await svc.TryFetchAsync("S", BookType.Comic, CancellationToken.None);

        Assert.Empty(meta);
    }
}
