using System.IO;
using System.Text.Json;

namespace ReadestStats;

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);

public static class WindowPlacementStore
{
    private static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReadestStats", "window.json");

    public static WindowPlacement? Load()
    {
        try
        {
            return File.Exists(PathName) ? JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(PathName)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public static void Save(WindowPlacement placement)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            File.WriteAllText(PathName, JsonSerializer.Serialize(placement, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
