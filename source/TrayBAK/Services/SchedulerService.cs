using System.IO;
using System.Windows.Threading;
using TrayBAK.Managers;
using TrayBAK.Models;

namespace TrayBAK.Services;

public enum JobStatus
{
    Idle,
    Queued,
    Running,
    Disabled
}

public sealed class SchedulerService
{
    private readonly AppSettings _settings;
    private readonly LogService _log;
    private readonly DispatcherTimer _timer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, CancellationTokenSource> _active = new();
    private readonly HashSet<Guid> _running = new();
    private readonly Dictionary<Guid, (int Attempt, DateTime Due)> _retries = new();

    public event Action<BackupJob>? JobStarted;
    public event Action<BackupJob, BackupResult>? JobFinished;
    public event Action? StateChanged;

    public SchedulerService(AppSettings settings, LogService log)
    {
        _settings = settings;
        _log = log;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += (_, _) => Tick();
    }

    public bool AnyActive => _active.Count > 0;

    public bool IsPaused => _settings.SchedulerPaused;

    public void Start()
    {
        var now = DateTime.Now;
        foreach (var job in _settings.Jobs)
            job.Anchor = job.RunMissed ? DateTime.MinValue : now;
        _timer.Start();
        Tick();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void CancelAll()
    {
        foreach (var cts in _active.Values.ToList())
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    public void Cancel(BackupJob job)
    {
        if (_active.TryGetValue(job.Id, out var cts))
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    public void SetPaused(bool paused)
    {
        _settings.SchedulerPaused = paused;
        SettingsManager.Save(_settings);
        _log.Add(LogLevel.Info, "", paused ? "Scheduling paused" : "Scheduling resumed");
        StateChanged?.Invoke();
        if (!paused) Tick();
    }

    public JobStatus GetStatus(BackupJob job)
    {
        if (_running.Contains(job.Id)) return JobStatus.Running;
        if (_active.ContainsKey(job.Id)) return JobStatus.Queued;
        if (!job.Enabled) return JobStatus.Disabled;
        return JobStatus.Idle;
    }

    public bool IsActive(BackupJob job) => _active.ContainsKey(job.Id);

    public DateTime? RetryDue(BackupJob job)
    {
        return _retries.TryGetValue(job.Id, out var retry) ? retry.Due : null;
    }

    public bool RunNow(BackupJob job, string trigger, bool scheduled = false, int retryAttempt = 0)
    {
        if (_active.ContainsKey(job.Id)) return false;
        _retries.Remove(job.Id);
        var cts = new CancellationTokenSource();
        _active[job.Id] = cts;
        StateChanged?.Invoke();
        _ = ExecuteAsync(job, trigger, scheduled, retryAttempt, cts);
        return true;
    }

    private void PlanRetry(BackupJob job, bool scheduled, int attempt)
    {
        if (!scheduled || !_settings.RetryFailedBackups) return;
        var max = Math.Max(1, _settings.RetryCount);
        if (attempt >= max)
        {
            _log.Add(LogLevel.Error, job.Name, $"Giving up after {max} retries. The next scheduled run will try again.");
            return;
        }
        var delay = Math.Max(1, _settings.RetryDelayMinutes);
        _retries[job.Id] = (attempt + 1, DateTime.Now.AddMinutes(delay));
        _log.Add(LogLevel.Warning, job.Name, $"Will retry in {delay} min (retry {attempt + 1} of {max})");
    }

    private async Task ExecuteAsync(BackupJob job, string trigger, bool scheduled, int retryAttempt, CancellationTokenSource cts)
    {
        try
        {
            await _gate.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            _active.Remove(job.Id);
            cts.Dispose();
            StateChanged?.Invoke();
            return;
        }

        try
        {
            _running.Add(job.Id);
            var started = DateTime.Now;
            _log.Add(LogLevel.Info, job.Name, $"Backup started ({trigger})");
            JobStarted?.Invoke(job);
            StateChanged?.Invoke();

            var result = await BackupService.RunAsync(job, cts.Token);

            job.LastRun = started;
            job.LastSeconds = result.Duration.TotalSeconds;
            job.LastMessage = result.Message;
            if (result.Success)
            {
                job.LastResult = "Success";
                job.LastSuccess = started;
                job.LastFiles = result.Files;
                job.LastSizeBytes = result.Bytes;
            }
            else
            {
                job.LastResult = result.Message == "Cancelled" ? "Cancelled" : "Failed";
            }

            var level = result.Success ? LogLevel.Success : (job.LastResult == "Cancelled" ? LogLevel.Warning : LogLevel.Error);
            var text = result.Success
                ? $"Backup completed in {result.Duration.TotalSeconds:N1}s: {result.Message}"
                : $"Backup {job.LastResult.ToLowerInvariant()}: {result.Message}";
            _log.Add(level, job.Name, text);
            if (!result.Success && job.LastResult == "Failed") PlanRetry(job, scheduled, retryAttempt);

            try { SettingsManager.Save(_settings); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            JobFinished?.Invoke(job, result);
        }
        finally
        {
            _running.Remove(job.Id);
            _active.Remove(job.Id);
            cts.Dispose();
            _gate.Release();
            StateChanged?.Invoke();
        }
    }

    private void Tick()
    {
        if (_settings.SchedulerPaused) return;
        var now = DateTime.Now;
        foreach (var job in _settings.Jobs.ToList())
        {
            if (!job.Enabled)
            {
                _retries.Remove(job.Id);
                continue;
            }
            if (_active.ContainsKey(job.Id)) continue;

            var next = job.Schedule == ScheduleType.Manual ? null : NextRun(job);
            if (next != null && next <= now)
            {
                if (!job.RunMissed && next < now.AddMinutes(-2))
                {
                    job.Anchor = now;
                }
                else
                {
                    RunNow(job, next < now.AddMinutes(-2) ? "scheduled, missed run" : "scheduled", true);
                    continue;
                }
            }

            if (_retries.TryGetValue(job.Id, out var retry) && retry.Due <= now)
                RunNow(job, $"scheduled retry {retry.Attempt} of {Math.Max(1, _settings.RetryCount)}", true, retry.Attempt);
        }
    }

    public static DateTime? NextRun(BackupJob job)
    {
        if (!job.Enabled || job.Schedule == ScheduleType.Manual) return null;

        var reference = job.CreatedAt;
        if (job.LastRun.HasValue && job.LastRun.Value > reference) reference = job.LastRun.Value;
        if (job.Anchor > reference) reference = job.Anchor;

        switch (job.Schedule)
        {
            case ScheduleType.Minutes:
                return reference.AddMinutes(Math.Max(1, job.IntervalMinutes));

            case ScheduleType.Hourly:
            {
                var c = new DateTime(reference.Year, reference.Month, reference.Day, reference.Hour, 0, 0)
                    .AddMinutes(Math.Clamp(job.MinutePastHour, 0, 59));
                while (c <= reference) c = c.AddHours(1);
                return c;
            }

            case ScheduleType.Daily:
            {
                var c = reference.Date + job.GetTimeOfDay();
                if (c <= reference) c = c.AddDays(1);
                return c;
            }

            case ScheduleType.Weekly:
            {
                if (job.Days.Count == 0) return null;
                for (var i = 0; i <= 7; i++)
                {
                    var day = reference.Date.AddDays(i);
                    if (!job.Days.Contains(day.DayOfWeek)) continue;
                    var c = day + job.GetTimeOfDay();
                    if (c > reference) return c;
                }
                return null;
            }
        }
        return null;
    }

    public static string Describe(BackupJob job)
    {
        var time = job.GetTimeOfDay().ToString(@"hh\:mm");
        switch (job.Schedule)
        {
            case ScheduleType.Manual:
                return "Manual only";
            case ScheduleType.Minutes:
                return $"Every {job.IntervalMinutes} min";
            case ScheduleType.Hourly:
                return $"Hourly at :{job.MinutePastHour:D2}";
            case ScheduleType.Daily:
                return $"Daily at {time}";
            case ScheduleType.Weekly:
                var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                    DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
                var names = order.Where(job.Days.Contains).Select(d => d.ToString()[..3]);
                return $"{string.Join(", ", names)} at {time}";
        }
        return "";
    }
}
