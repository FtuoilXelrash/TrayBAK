namespace TrayBAK.Models;

public class AppSettings
{
    public const int MaxJobs = 10;

    public List<BackupJob> Jobs { get; set; } = new();

    public string Theme { get; set; } = "System";
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool PopupAlerts { get; set; } = true;
    public bool SoundAlerts { get; set; } = true;
    public int LogKeepDays { get; set; } = 90;
    public bool RetryFailedBackups { get; set; } = true;
    public int RetryCount { get; set; } = 3;
    public int RetryDelayMinutes { get; set; } = 10;
    public bool SchedulerPaused { get; set; }

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 900;
    public double WindowHeight { get; set; } = 560;
}
