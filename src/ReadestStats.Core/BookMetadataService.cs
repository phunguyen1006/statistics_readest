using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ReadestStats.Core;

public sealed class BookMetadataService
{
    private readonly HttpClient _http;
    private readonly Func<string?> _googleApiKey;

    public BookMetadataService(HttpClient? httpClient = null, Func<string?>? googleApiKey = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ReadestStats/1.8 (+https://github.com/phunguyen1006/statistics_readest)");
        _googleApiKey = googleApiKey ?? (() => null);
    }

    public async Task<IReadOnlyList<BookMetadataResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        query = query.Trim();
        if (query.Length < 2) return [];
        var googleKey = _googleApiKey();
        var googleTask = TrySearchAsync(() => SearchGoogleAsync(query, googleKey, cancellationToken));
        var openLibraryTask = TrySearchAsync(() => SearchOpenLibraryAsync(query, cancellationToken));
        await Task.WhenAll(googleTask, openLibraryTask);
        return googleTask.Result.Concat(openLibraryTask.Result)
            .GroupBy(item => !string.IsNullOrWhiteSpace(item.Isbn13) ? "isbn:" + item.Isbn13 : $"{item.Title}|{item.Authors}|{item.PublishedDate}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(ResultQuality).First())
            .OrderByDescending(item => Relevance(item, query))
            .ThenByDescending(ResultQuality)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(60)
            .ToArray();
    }

    public static int Relevance(BookMetadataResult item, string query)
    {
        static string Normalize(string value) => new(value.Normalize(System.Text.NormalizationForm.FormD)
            .Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(char.ToLowerInvariant).ToArray());
        var needle = Normalize(query.Trim());
        var title = Normalize(item.Title);
        var authors = Normalize(item.Authors);
        var score = title == needle ? 1000 : title.StartsWith(needle, StringComparison.Ordinal) ? 700 : title.Contains(needle, StringComparison.Ordinal) ? 450 : 0;
        score += authors.Contains(needle, StringComparison.Ordinal) ? 250 : 0;
        var tokens = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        score += tokens.Count(token => title.Contains(token, StringComparison.Ordinal)) * 35;
        if (needle.Replace("-", "", StringComparison.Ordinal).All(char.IsDigit) && (item.Isbn10 == needle || item.Isbn13 == needle)) score += 1200;
        return score;
    }

    private static int ResultQuality(BookMetadataResult item) =>
        (item.PageCount is > 0 ? 5 : 0) + (item.CoverUrl is not null ? 4 : 0) +
        (!string.IsNullOrWhiteSpace(item.Authors) ? 3 : 0) + (item.Isbn13 is not null ? 2 : 0) +
        (item.Publisher is not null ? 1 : 0);

    private static async Task<IReadOnlyList<BookMetadataResult>> TrySearchAsync(Func<Task<IReadOnlyList<BookMetadataResult>>> search)
    {
        try { return await search(); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { return []; }
    }

    private async Task<IReadOnlyList<BookMetadataResult>> SearchOpenLibraryAsync(string query, CancellationToken cancellationToken)
    {
        var fields = "key,title,author_name,first_publish_year,publisher,isbn,language,cover_i,edition_key,number_of_pages_median";
        var url = $"https://openlibrary.org/search.json?q={Uri.EscapeDataString(query)}&limit=40&fields={Uri.EscapeDataString(fields)}";
        var response = await _http.GetFromJsonAsync<OpenLibraryResponse>(url, cancellationToken);
        return response?.Docs?.Select(doc =>
        {
            var isbn13 = doc.Isbn?.FirstOrDefault(value => value.Length == 13);
            var isbn10 = doc.Isbn?.FirstOrDefault(value => value.Length == 10);
            var cover = doc.CoverId is > 0 ? $"https://covers.openlibrary.org/b/id/{doc.CoverId}-M.jpg?default=false" : null;
            var externalId = doc.EditionKeys?.FirstOrDefault() ?? doc.Key?.TrimStart('/') ?? Guid.NewGuid().ToString("N");
            return new BookMetadataResult("Open Library", externalId, doc.Title ?? "Untitled", string.Join(", ", doc.Authors ?? []), doc.Publishers?.FirstOrDefault(), doc.FirstPublishYear?.ToString(), doc.PageCount, isbn10, isbn13, doc.Languages?.FirstOrDefault(), cover, doc.Key is null ? null : "https://openlibrary.org" + doc.Key);
        }).ToArray() ?? [];
    }

    private async Task<IReadOnlyList<BookMetadataResult>> SearchGoogleAsync(string query, string? apiKey, CancellationToken cancellationToken)
    {
        var url = $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString(query)}&maxResults=40&printType=books";
        if (!string.IsNullOrWhiteSpace(apiKey)) url += $"&key={Uri.EscapeDataString(apiKey)}";
        var response = await _http.GetFromJsonAsync<GoogleBooksResponse>(url, cancellationToken);
        return response?.Items?.Select(item =>
        {
            var info = item.VolumeInfo ?? new();
            string? Identifier(string type) => info.Identifiers?.FirstOrDefault(value => value.Type == type)?.Identifier;
            return new BookMetadataResult("Google Books", item.Id ?? Guid.NewGuid().ToString("N"), info.Title ?? "Untitled", string.Join(", ", info.Authors ?? []), info.Publisher, info.PublishedDate, info.PageCount, Identifier("ISBN_10"), Identifier("ISBN_13"), info.Language, info.Images?.Thumbnail?.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase), info.InfoLink, info.Description);
        }).ToArray() ?? [];
    }

    private sealed class OpenLibraryResponse { [JsonPropertyName("docs")] public List<OpenLibraryDocument>? Docs { get; set; } }
    private sealed class OpenLibraryDocument
    {
        [JsonPropertyName("key")] public string? Key { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("author_name")] public List<string>? Authors { get; set; }
        [JsonPropertyName("first_publish_year")] public int? FirstPublishYear { get; set; }
        [JsonPropertyName("publisher")] public List<string>? Publishers { get; set; }
        [JsonPropertyName("isbn")] public List<string>? Isbn { get; set; }
        [JsonPropertyName("language")] public List<string>? Languages { get; set; }
        [JsonPropertyName("cover_i")] public long? CoverId { get; set; }
        [JsonPropertyName("edition_key")] public List<string>? EditionKeys { get; set; }
        [JsonPropertyName("number_of_pages_median")] public int? PageCount { get; set; }
    }
    private sealed class GoogleBooksResponse { [JsonPropertyName("items")] public List<GoogleBookItem>? Items { get; set; } }
    private sealed class GoogleBookItem { [JsonPropertyName("id")] public string? Id { get; set; } [JsonPropertyName("volumeInfo")] public GoogleVolumeInfo? VolumeInfo { get; set; } }
    private sealed class GoogleVolumeInfo
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("authors")] public List<string>? Authors { get; set; }
        [JsonPropertyName("publisher")] public string? Publisher { get; set; }
        [JsonPropertyName("publishedDate")] public string? PublishedDate { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("industryIdentifiers")] public List<GoogleIdentifier>? Identifiers { get; set; }
        [JsonPropertyName("pageCount")] public int? PageCount { get; set; }
        [JsonPropertyName("language")] public string? Language { get; set; }
        [JsonPropertyName("imageLinks")] public GoogleImages? Images { get; set; }
        [JsonPropertyName("infoLink")] public string? InfoLink { get; set; }
    }
    private sealed class GoogleIdentifier { [JsonPropertyName("type")] public string? Type { get; set; } [JsonPropertyName("identifier")] public string? Identifier { get; set; } }
    private sealed class GoogleImages { [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; } }
}
