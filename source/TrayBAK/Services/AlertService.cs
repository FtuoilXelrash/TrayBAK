using System.Media;
using TrayBAK.Models;
using TrayBAK.Windows;

namespace TrayBAK.Services;

public sealed class AlertService
{
    private readonly AppSettings _settings;
    private readonly SchedulerService _scheduler;

    public AlertService(AppSettings settings, SchedulerService scheduler)
    {
        _settings = settings;
        _scheduler = scheduler;
        scheduler.JobFinished += OnJobFinished;
    }

    private void OnJobFinished(BackupJob job, BackupResult result)
    {
        if (result.Message == "Cancelled") return;
        if (!result.Success && _scheduler.RetryDue(job) != null) return;
        var wanted = result.Success ? job.AlertOnSuccess : job.AlertOnFailure;
        if (!wanted) return;

        if (_settings.PopupAlerts)
        {
            var title = result.Success ? $"Backup complete: {job.Name}" : $"Backup failed: {job.Name}";
            AlertPopupWindow.ShowAlert(title, result.Message, result.Success ? LogLevel.Success : LogLevel.Error);
        }
        if (_settings.SoundAlerts)
        {
            (result.Success ? SystemSounds.Asterisk : SystemSounds.Hand).Play();
        }
    }
}
