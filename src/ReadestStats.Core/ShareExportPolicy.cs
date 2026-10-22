using System.Text.Json;

namespace ReadestStats.Core;

/// <summary>Privacy defaults for shareable exports. Archival backups retain their existing format.</summary>
public static class ShareExportPolicy
{
    /// <summary>Quote a CSV text cell and prevent spreadsheet formula interpretation.</summary>
    public static string CsvCell(string? value)
    {
        var text = (value ?? "").Replace("\0", "");
        var index = 0;
        while (index < text.Length && (char.IsWhiteSpace(text[index]) || char.IsControl(text[index]) || text[index] == '\uFEFF')) index++;
        if (text.Length > 0 && (text[0] is '\t' or '\r' or '\n'
            || index < text.Length && (text[index] is '=' or '+' or '-' or '@'))) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>Do not disclose local drive, UNC, file URI, or non-web cover locations.</summary>
    public static string? PublicCoverUrl(string? value) =>
        ExternalLinkPolicy.TryGetWebUri(value, out var uri) ? uri!.AbsoluteUri : null;

    /// <summary>Return a detached share copy without private notes or machine-local paths by default.</summary>
    public static ManualReadingData ShareLibrary(ManualReadingData source, bool includeSessionNotes = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = JsonSerializer.Deserialize<ManualReadingData>(JsonSerializer.SerializeToUtf8Bytes(source))!;
        copy.ActiveSession = null;
        copy.RecoveryReferences = null;
        copy.Books ??= [];
        copy.Sessions ??= [];
        foreach (var book in copy.Books)
        {
            book.CoverUrl = PublicCoverUrl(book.CoverUrl);
            book.InfoUrl = PublicCoverUrl(book.InfoUrl);
            book.GoodreadsUrl = PublicCoverUrl(book.GoodreadsUrl);
        }
        if (!includeSessionNotes) foreach (var session in copy.Sessions) session.Note = "";
        return copy;
    }
}
