namespace BooksMetadataBaker.Services.Integration;

public static class HtmlCleaner
{
    public static string StripHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var result = Regex.Replace(value, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<[^>]+>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(result).Trim();
    }
}
