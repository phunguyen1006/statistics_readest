using System.IO;

namespace ReadestStats.Services;

public sealed class FileAppLogger : IAppLogger
{
    private readonly string _logPath;
    private readonly string _logDirectory;

    public FileAppLogger(string? customDirectory = null)
    {
        _logDirectory = customDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ReadestStats", "logs");
        _logPath = Path.Combine(_logDirectory, "readest-stats.log");
    }

    public void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 1_000_000)
            {
                for (var index = 2; index >= 1; index--)
                {
                    var source = Path.Combine(_logDirectory, $"readest-stats.{index}.log");
                    var destination = Path.Combine(_logDirectory, $"readest-stats.{index + 1}.log");
                    if (File.Exists(source)) File.Move(source, destination, true);
                }
                File.Move(_logPath, Path.Combine(_logDirectory, "readest-stats.1.log"), true);
            }
            File.AppendAllText(_logPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
