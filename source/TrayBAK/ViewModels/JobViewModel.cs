using System.Windows;
using System.Windows.Media;
using TrayBAK.Models;
using TrayBAK.Services;

namespace TrayBAK.ViewModels;

public sealed class JobViewModel : ViewModelBase
{
    private readonly SchedulerService _scheduler;

    public JobViewModel(BackupJob job, SchedulerService scheduler)
    {
        Job = job;
        _scheduler = scheduler;
    }

    public BackupJob Job { get; }

    public string Name => Job.Name;

    public bool Enabled
    {
        get => Job.Enabled;
        set
        {
            Job.Enabled = value;
            Job.Anchor = DateTime.Now;
            App.Instance.SaveSettings();
            RaiseAll();
        }
    }

    public string ScheduleText => SchedulerService.Describe(Job);

    public string StatusText => _scheduler.GetStatus(Job) switch
    {
        JobStatus.Running => "Running",
        JobStatus.Queued => "Queued",
        JobStatus.Disabled => "Disabled",
        _ => "Idle"
    };

    public string LastRunText => Job.LastRun?.ToString("MM-dd-yyyy HH:mm") ?? "Never";

    public string ResultText => string.IsNullOrEmpty(Job.LastResult) ? "-" : Job.LastResult;

    public string SizeText => Job.LastSizeBytes > 0 ? BackupService.FormatSize(Job.LastSizeBytes) : "-";

    public string NextRunText
    {
        get
        {
            if (Job.Schedule == ScheduleType.Manual) return "Manual";
            if (!Job.Enabled) return "-";
            if (_scheduler.IsPaused) return "Paused";
            if (_scheduler.RetryDue(Job) is { } retryDue) return $"Retry {retryDue:HH:mm}";
            var next = SchedulerService.NextRun(Job);
            return next?.ToString("MM-dd-yyyy HH:mm") ?? "-";
        }
    }

    public string Details => Job.LastMessage;

    public string LastLine => Job.LastRun == null
        ? "Never run"
        : $"Last: {LastRunText} - {ResultText}" + (Job.LastSizeBytes > 0 ? $", {SizeText}" : "");

    public string NextLine => "Next: " + NextRunText;

    public Brush ResultBrush
    {
        get
        {
            var key = _scheduler.GetStatus(Job) == JobStatus.Running ? "RunningBrush"
                : Job.LastResult switch
                {
                    "Success" => "SuccessBrush",
                    "Failed" => "ErrorBrush",
                    "Cancelled" => "WarnBrush",
                    _ => "MutedBrush"
                };
            return (Brush)Application.Current.FindResource(key);
        }
    }
}
