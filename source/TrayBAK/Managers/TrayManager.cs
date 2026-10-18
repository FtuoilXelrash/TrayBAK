using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using TrayBAK.Models;
using TrayBAK.Services;
using TrayBAK.Windows;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace TrayBAK.Managers;

public enum TrayState
{
    Idle,
    Running,
    Error,
    Paused
}

public sealed class TrayManager : IDisposable
{
    private readonly AppSettings _settings;
    private readonly SchedulerService _scheduler;
    private readonly WinForms.NotifyIcon _icon;
    private readonly Drawing.Icon _baseIcon;
    private readonly Dictionary<TrayState, Drawing.Icon> _icons = new();
    private readonly WinForms.Timer _clickTimer;
    private readonly WinForms.Timer _refreshTimer;
    private StatusPopupWindow? _popup;
    private DateTime _popupClosedAt = DateTime.MinValue;
    private TrayState? _currentState;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayManager(AppSettings settings, SchedulerService scheduler)
    {
        _settings = settings;
        _scheduler = scheduler;
        _baseIcon = LoadBaseIcon();

        _icon = new WinForms.NotifyIcon
        {
            Icon = _baseIcon,
            Text = "TrayBAK",
            Visible = true,
            ContextMenuStrip = new WinForms.ContextMenuStrip()
        };
        BuildMenu(_icon.ContextMenuStrip);
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu(_icon.ContextMenuStrip);

        _clickTimer = new WinForms.Timer { Interval = WinForms.SystemInformation.DoubleClickTime };
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            TogglePopup();
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) _clickTimer.Start();
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button != WinForms.MouseButtons.Left) return;
            _clickTimer.Stop();
            OpenRequested?.Invoke();
        };

        _refreshTimer = new WinForms.Timer { Interval = 30000 };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        _scheduler.StateChanged += Refresh;
        Refresh();
    }

    public static TrayState ComputeState(AppSettings settings, SchedulerService scheduler)
    {
        if (scheduler.AnyActive) return TrayState.Running;
        if (settings.Jobs.Any(j => j.Enabled && j.LastResult == "Failed")) return TrayState.Error;
        if (settings.SchedulerPaused) return TrayState.Paused;
        return TrayState.Idle;
    }

    public void Refresh()
    {
        var state = ComputeState(_settings, _scheduler);
        if (_currentState != state)
        {
            _currentState = state;
            _icon.Icon = GetIcon(state);
        }
        _icon.Text = BuildTooltip(state);
    }

    private static Drawing.Icon LoadBaseIcon()
    {
        var uri = new Uri("pack://application:,,,/resource/TrayBAK.ico");
        using var stream = Application.GetResourceStream(uri).Stream;
        return new Drawing.Icon(stream, new Drawing.Size(32, 32));
    }

    private Drawing.Icon GetIcon(TrayState state)
    {
        if (state == TrayState.Idle) return _baseIcon;
        if (_icons.TryGetValue(state, out var cached)) return cached;

        var color = state switch
        {
            TrayState.Running => Drawing.Color.FromArgb(47, 129, 247),
            TrayState.Error => Drawing.Color.FromArgb(248, 81, 73),
            _ => Drawing.Color.FromArgb(139, 148, 158)
        };

        using var bitmap = _baseIcon.ToBitmap();
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var rect = new Drawing.Rectangle(16, 16, 15, 15);
            using var fill = new Drawing.SolidBrush(color);
            using var border = new Drawing.Pen(Drawing.Color.White, 2);
            g.FillEllipse(fill, rect);
            g.DrawEllipse(border, rect);
        }

        var handle = bitmap.GetHicon();
        var icon = (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
        NativeMethods.DestroyIcon(handle);
        _icons[state] = icon;
        return icon;
    }

    private string BuildTooltip(TrayState state)
    {
        var sb = new StringBuilder("TrayBAK");
        sb.Append(state switch
        {
            TrayState.Running => " - backup running",
            TrayState.Error => " - last backup failed",
            TrayState.Paused => " - scheduling paused",
            _ => ""
        });

        foreach (var job in _settings.Jobs.Take(4))
        {
            var last = job.LastRun == null ? "never run" : $"{job.LastResult} {job.LastRun:MM-dd HH:mm}";
            sb.Append('\n').Append(Shorten(job.Name, 18)).Append(": ").Append(last);
        }

        var next = _settings.Jobs
            .Select(j => new { Job = j, Next = SchedulerService.NextRun(j) })
            .Where(x => x.Next.HasValue)
            .OrderBy(x => x.Next)
            .FirstOrDefault();
        if (next != null && !_settings.SchedulerPaused)
            sb.Append("\nNext: ").Append(Shorten(next.Job.Name, 18)).Append(' ').Append(next.Next!.Value.ToString("MM-dd HH:mm"));

        var text = sb.ToString();
        return text.Length > 127 ? text[..124] + "..." : text;
    }

    private static string Shorten(string value, int max)
    {
        return value.Length <= max ? value : value[..(max - 1)] + "~";
    }

    private void BuildMenu(WinForms.ContextMenuStrip menu)
    {
        var old = menu.Items.Cast<WinForms.ToolStripItem>().ToList();
        menu.Items.Clear();
        foreach (var item in old) item.Dispose();

        try
        {
            PopulateMenu(menu);
        }
        catch (Exception ex)
        {
            App.Instance.Log.Add(LogLevel.Error, "", "Tray menu error: " + ex.Message);
            foreach (var item in menu.Items.Cast<WinForms.ToolStripItem>().ToList()) item.Dispose();
            menu.Items.Clear();
            var open = new WinForms.ToolStripMenuItem("Open TrayBAK");
            open.Click += (_, _) => OpenRequested?.Invoke();
            var exit = new WinForms.ToolStripMenuItem("Exit");
            exit.Click += (_, _) => ExitRequested?.Invoke();
            menu.Items.Add(open);
            menu.Items.Add(exit);
        }
    }

    private void PopulateMenu(WinForms.ContextMenuStrip menu)
    {

        var open = new WinForms.ToolStripMenuItem("Open TrayBAK") { Font = new Drawing.Font(menu.Font, Drawing.FontStyle.Bold) };
        open.Click += (_, _) => OpenRequested?.Invoke();
        menu.Items.Add(open);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var run = new WinForms.ToolStripMenuItem("Run backup now");
        var status = new WinForms.ToolStripMenuItem("Job status");
        if (_settings.Jobs.Count == 0)
        {
            run.DropDownItems.Add(new WinForms.ToolStripMenuItem("No jobs configured") { Enabled = false });
            status.DropDownItems.Add(new WinForms.ToolStripMenuItem("No jobs configured") { Enabled = false });
        }
        foreach (var job in _settings.Jobs)
        {
            var captured = job;
            var active = _scheduler.IsActive(captured);
            var runItem = new WinForms.ToolStripMenuItem($"{captured.Name}   ({ResultLabel(captured)})") { Enabled = !active };
            runItem.Click += (_, _) => _scheduler.RunNow(captured, "manual, tray");
            run.DropDownItems.Add(runItem);

            var jobItem = new WinForms.ToolStripMenuItem(captured.Name + (active ? "   (running)" : ""));
            jobItem.DropDownItems.Add(new WinForms.ToolStripMenuItem("Schedule: " + SchedulerService.Describe(captured)) { Enabled = false });
            jobItem.DropDownItems.Add(new WinForms.ToolStripMenuItem("Last run: " + ResultLabel(captured)) { Enabled = false });
            if (captured.LastSizeBytes > 0)
                jobItem.DropDownItems.Add(new WinForms.ToolStripMenuItem($"Last backup: {captured.LastFiles} files, {BackupService.FormatSize(captured.LastSizeBytes)}") { Enabled = false });
            var next = SchedulerService.NextRun(captured);
            jobItem.DropDownItems.Add(new WinForms.ToolStripMenuItem("Next run: " + (captured.Schedule == ScheduleType.Manual ? "manual"
                : next.HasValue ? next.Value.ToString("MM-dd-yyyy HH:mm") : "-")) { Enabled = false });
            jobItem.DropDownItems.Add(new WinForms.ToolStripSeparator());
            var folder = new WinForms.ToolStripMenuItem("Open backup folder");
            folder.Click += (_, _) => OpenFolder(BackupService.FolderToOpen(captured));
            jobItem.DropDownItems.Add(folder);
            status.DropDownItems.Add(jobItem);
        }
        if (_settings.Jobs.Count > 0)
        {
            run.DropDownItems.Add(new WinForms.ToolStripSeparator());
            var all = new WinForms.ToolStripMenuItem("All enabled jobs");
            all.Click += (_, _) =>
            {
                foreach (var job in _settings.Jobs.Where(j => j.Enabled)) _scheduler.RunNow(job, "manual, tray, all jobs");
            };
            run.DropDownItems.Add(all);
        }
        menu.Items.Add(run);
        menu.Items.Add(status);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        var pause = new WinForms.ToolStripMenuItem(_scheduler.IsPaused ? "Resume scheduling" : "Pause scheduling");
        pause.Click += (_, _) => _scheduler.SetPaused(!_scheduler.IsPaused);
        menu.Items.Add(pause);

        var log = new WinForms.ToolStripMenuItem("View log");
        log.Click += (_, _) => App.Instance.ShowLog();
        menu.Items.Add(log);
        var options = new WinForms.ToolStripMenuItem("Options");
        options.Click += (_, _) => App.Instance.ShowOptions();
        menu.Items.Add(options);
        var about = new WinForms.ToolStripMenuItem("Help and About");
        about.Click += (_, _) => App.Instance.ShowAbout();
        menu.Items.Add(about);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        var exit = new WinForms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exit);
    }

    private static string ResultLabel(BackupJob job)
    {
        return job.LastRun == null ? "never run" : $"{job.LastResult} {job.LastRun:MM-dd HH:mm}";
    }

    private static void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void TogglePopup()
    {
        if (_popup != null)
        {
            _popup.Close();
            return;
        }
        if ((DateTime.Now - _popupClosedAt).TotalMilliseconds < 400) return;

        _popup = new StatusPopupWindow(_settings, _scheduler, ComputeState(_settings, _scheduler));
        _popup.Closed += (_, _) =>
        {
            _popup = null;
            _popupClosedAt = DateTime.Now;
        };
        _popup.Show();
        _popup.Activate();
    }

    public void Dispose()
    {
        _clickTimer.Dispose();
        _refreshTimer.Dispose();
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        foreach (var icon in _icons.Values) icon.Dispose();
        _baseIcon.Dispose();
    }
}
