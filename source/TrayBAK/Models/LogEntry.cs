using System.Text.Json.Serialization;

namespace TrayBAK.Models;

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public class LogEntry
{
    public DateTime Time { get; set; } = DateTime.Now;
    public LogLevel Level { get; set; } = LogLevel.Info;
    public string Job { get; set; } = "";
    public string Message { get; set; } = "";

    [JsonIgnore]
    public string TimeText => Time.ToString("MM-dd-yyyy HH:mm:ss");
}
