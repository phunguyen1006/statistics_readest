using System.Globalization;
using System.Text;

namespace ReadestStats.Core;

public static class SearchRanking
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var decomposed = value.Normalize(NormalizationForm.FormD);
        return new string(decomposed.Where(character => (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character)) || char.IsWhiteSpace(character)).Select(char.ToLowerInvariant).ToArray()).Normalize(NormalizationForm.FormC);
    }

    public static int Score(string query, string title, string detail, string kind)
    {
        var needle = Normalize(query); if (needle.Length == 0) return 1;
        var normalizedTitle = Normalize(title); var normalizedDetail = Normalize(detail); var normalizedKind = Normalize(kind);
        if (normalizedTitle == needle) return 1000;
        if (normalizedTitle.StartsWith(needle, StringComparison.Ordinal)) return 800;
        if (normalizedTitle.Contains(needle, StringComparison.Ordinal)) return 600;
        if (normalizedDetail.Contains(needle, StringComparison.Ordinal)) return 350;
        if (normalizedKind.Contains(needle, StringComparison.Ordinal)) return 200;
        var tokens = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 && tokens.All(token => normalizedTitle.Contains(token, StringComparison.Ordinal) || normalizedDetail.Contains(token, StringComparison.Ordinal)) ? 150 : 0;
    }
}
