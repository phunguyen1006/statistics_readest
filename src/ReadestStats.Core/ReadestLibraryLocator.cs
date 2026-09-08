namespace ReadestStats.Core;

public static class ReadestLibraryLocator
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".epub", ".pdf", ".mobi", ".azw", ".azw3", ".fb2", ".cbz", ".cbr", ".txt", ".md", ".html", ".htm", ".docx"
    };

    private static readonly string[] CoverFileNames =
    {
        "cover.png", "cover.jpg", "cover.jpeg", "cover.webp", "cover.bmp"
    };

    public static string? FindBookFile(string? databasePath, string? bookHash)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(bookHash)) return null;
        try
        {
            var root = Path.GetDirectoryName(Path.GetFullPath(databasePath));
            if (root is null) return null;
            var bookDirectory = Path.Combine(root, "Books", bookHash);
            if (!Directory.Exists(bookDirectory)) return null;
            return Directory.EnumerateFiles(bookDirectory)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    public static string? FindCoverFile(string? databasePath, string? bookHash)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(bookHash)) return null;
        try
        {
            var root = Path.GetDirectoryName(Path.GetFullPath(databasePath));
            if (root is null) return null;
            var bookDirectory = Path.Combine(root, "Books", bookHash);
            if (!Directory.Exists(bookDirectory)) return null;
            foreach (var fileName in CoverFileNames)
            {
                var candidate = Path.Combine(bookDirectory, fileName);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    public static string? FindReadestExecutable(string? localAppData = null)
    {
        var local = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(local, "Programs", "Readest", "readest.exe"),
            Path.Combine(local, "Readest", "readest.exe"),
            Path.Combine(local, "Programs", "readest.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
