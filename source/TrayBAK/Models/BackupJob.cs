using System.Text.Json.Serialization;

namespace TrayBAK.Models;

public enum ScheduleType
{
    Manual,
    Minutes,
    Hourly,
    Daily,
    Weekly
}

public class BackupJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Job";
    public bool Enabled { get; set; } = true;
    public string SourceFolder { get; set; } = "";
    public string DestinationFolder { get; set; } = "";
    public List<string> Excludes { get; set; } = new();
    public List<string> ExcludedPaths { get; set; } = new();

    public ScheduleType Schedule { get; set; } = ScheduleType.Daily;
    public int IntervalMinutes { get; set; } = 60;
    public int MinutePastHour { get; set; }
    public string TimeOfDay { get; set; } = "12:00";
    public List<DayOfWeek> Days { get; set; } = new();
    public bool RunMissed { get; set; } = true;

    public int KeepDays { get; set; } = 30;
    public int KeepCount { get; set; }

    public bool AlertOnSuccess { get; set; }
    public bool AlertOnFailure { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastRun { get; set; }
    public DateTime? LastSuccess { get; set; }
    public string LastResult { get; set; } = "";
    public string LastMessage { get; set; } = "";
    public long LastSizeBytes { get; set; }
    public int LastFiles { get; set; }
    public double LastSeconds { get; set; }

    [JsonIgnore]
    public DateTime Anchor { get; set; } = DateTime.MinValue;

    public TimeSpan GetTimeOfDay()
    {
        return TimeSpan.TryParse(TimeOfDay, out var t) ? t : new TimeSpan(12, 0, 0);
    }

    public BackupJob Clone()
    {
        return new BackupJob
        {
            Id = Id,
            Name = Name,
            Enabled = Enabled,
            SourceFolder = SourceFolder,
            DestinationFolder = DestinationFolder,
            Excludes = new List<string>(Excludes),
            ExcludedPaths = new List<string>(ExcludedPaths),
            Schedule = Schedule,
            IntervalMinutes = IntervalMinutes,
            MinutePastHour = MinutePastHour,
            TimeOfDay = TimeOfDay,
            Days = new List<DayOfWeek>(Days),
            RunMissed = RunMissed,
            KeepDays = KeepDays,
            KeepCount = KeepCount,
            AlertOnSuccess = AlertOnSuccess,
            AlertOnFailure = AlertOnFailure,
            CreatedAt = CreatedAt,
            LastRun = LastRun,
            LastSuccess = LastSuccess,
            LastResult = LastResult,
            LastMessage = LastMessage,
            LastSizeBytes = LastSizeBytes,
            LastFiles = LastFiles,
            LastSeconds = LastSeconds,
            Anchor = Anchor
        };
    }
}
