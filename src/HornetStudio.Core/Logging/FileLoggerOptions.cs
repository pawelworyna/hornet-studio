using Microsoft.Extensions.Logging;

namespace HornetStudio.Core.Logging;

public class FileLoggerOptions
{
    public string LogDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HornetStudio",
        "logs");

    public string FilePrefix { get; set; } = "hornet_";

    public LogLevel MinLogLevel { get; set; } = LogLevel.Information;

    public int RetainedDays { get; set; } = 14;

    public long MaxFileSizeMb { get; set; } = 10;
}
