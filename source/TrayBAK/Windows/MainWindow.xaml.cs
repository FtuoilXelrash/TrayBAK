using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using TrayBAK.Models;
using TrayBAK.Services;
using TrayBAK.ViewModels;

namespace TrayBAK.Windows;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<JobViewModel> _jobs = new();
    private readonly DispatcherTimer _refresh;

    public MainWindow()
    {
        InitializeComponent();
        var app = App.Instance;
        JobGrid.ItemsSource = _jobs;
        LogGrid.ItemsSource = app.Log.Entries;
        PauseMenu.IsChecked = app.Settings.SchedulerPaused;
        RestorePosition();
        ReloadJobs();

        app.Scheduler.StateChanged += RefreshAll;
        app.Log.EntryAdded += _ => ScrollLog();

        _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refresh.Tick += (_, _) => RefreshAll();
        _refresh.Start();
        RefreshAll();
    }

    private AppSettings Settings => App.Instance.Settings;
    private SchedulerService Scheduler => App.Instance.Scheduler;

    private JobViewModel? Selected => JobGrid.SelectedItem as JobViewModel;

    public void ReloadJobs()
    {
        _jobs.Clear();
        foreach (var job in Settings.Jobs) _jobs.Add(new JobViewModel(job, Scheduler));
        RefreshAll();
    }

    private void RefreshAll()
    {
        foreach (var vm in _jobs) vm.RaiseAll();

        var next = Settings.Jobs
            .Select(j => new { Job = j, Next = SchedulerService.NextRun(j) })
            .Where(x => x.Next.HasValue)
            .OrderBy(x => x.Next)
            .FirstOrDefault();

        var parts = new List<string> { $"{Settings.Jobs.Count} of {AppSettings.MaxJobs} jobs" };
        parts.Add(Scheduler.IsPaused ? "Scheduling paused"
            : next != null ? $"Next: {next.Job.Name} at {next.Next:MM-dd-yyyy HH:mm}" : "Nothing scheduled");
        if (Scheduler.AnyActive) parts.Add("Backup in progress");
        StatusText.Text = string.Join("   |   ", parts);

        var sel = Selected;
        var has = sel != null;
        var active = has && Scheduler.IsActive(sel!.Job);
        EditButton.IsEnabled = has && !active;
        DeleteButton.IsEnabled = has && !active;
        RunButton.IsEnabled = has && !active;
        StopButton.IsEnabled = active;
        FolderButton.IsEnabled = has;
        PauseMenu.IsChecked = Scheduler.IsPaused;
    }

    private void ScrollLog()
    {
        if (LogGrid.Items.Count > 0) LogGrid.ScrollIntoView(LogGrid.Items[^1]);
    }

    private void JobGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RefreshAll();
    }

    private void JobGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Selected != null && e.OriginalSource is FrameworkElement { DataContext: JobViewModel })
            Edit_Click(sender, e);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Settings.Jobs.Count >= AppSettings.MaxJobs)
        {
            MessageBox.Show(this, $"A maximum of {AppSettings.MaxJobs} jobs is supported.", "TrayBAK",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var job = new BackupJob { Name = NextJobName(), CreatedAt = DateTime.Now };
        var dialog = new JobEditorWindow(job, Settings.Jobs.Select(j => j.Name), true) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var created = dialog.Result;
        created.CreatedAt = DateTime.Now;
        Settings.Jobs.Add(created);
        App.Instance.SaveSettings();
        App.Instance.Log.Add(LogLevel.Info, created.Name, "Job created");
        ReloadJobs();
        App.Instance.NotifyJobsChanged();
    }

    private string NextJobName()
    {
        for (var i = 1; ; i++)
        {
            var name = $"Job {i}";
            if (!Settings.Jobs.Any(j => string.Equals(j.Name, name, StringComparison.OrdinalIgnoreCase))) return name;
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        var vm = Selected;
        if (vm == null) return;
        if (Scheduler.IsActive(vm.Job))
        {
            MessageBox.Show(this, "This job is running. Wait for it to finish or stop it first.", "TrayBAK",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var others = Settings.Jobs.Where(j => j != vm.Job).Select(j => j.Name);
        var dialog = new JobEditorWindow(vm.Job, others, false) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var index = Settings.Jobs.IndexOf(vm.Job);
        if (index < 0) return;
        Settings.Jobs[index] = dialog.Result;
        App.Instance.SaveSettings();
        App.Instance.Log.Add(LogLevel.Info, dialog.Result.Name, "Job updated");
        ReloadJobs();
        App.Instance.NotifyJobsChanged();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var vm = Selected;
        if (vm == null) return;
        var answer = MessageBox.Show(this,
            $"Delete the job \"{vm.Name}\"?\n\nExisting backup files in the backup folder are not deleted.",
            "TrayBAK", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        Settings.Jobs.Remove(vm.Job);
        App.Instance.SaveSettings();
        App.Instance.Log.Add(LogLevel.Info, vm.Name, "Job deleted");
        ReloadJobs();
        App.Instance.NotifyJobsChanged();
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        var vm = Selected;
        if (vm == null) return;
        if (!Scheduler.RunNow(vm.Job, "manual"))
            MessageBox.Show(this, "This job is already running.", "TrayBAK", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RunAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in Settings.Jobs.Where(j => j.Enabled))
            Scheduler.RunNow(job, "manual, all jobs");
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        var vm = Selected;
        if (vm != null) Scheduler.Cancel(vm.Job);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var vm = Selected;
        if (vm == null) return;
        var path = BackupService.FolderToOpen(vm.Job);
        if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        else
            MessageBox.Show(this, "The backup folder does not exist yet.", "TrayBAK", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        Scheduler.SetPaused(PauseMenu.IsChecked);
    }

    private void Log_Click(object sender, RoutedEventArgs e) => App.Instance.ShowLog();

    private void Options_Click(object sender, RoutedEventArgs e) => App.Instance.ShowOptions();

    private void About_Click(object sender, RoutedEventArgs e) => App.Instance.ShowAbout();

    private void Help_Click(object sender, RoutedEventArgs e) => App.Instance.OpenHelp();

    private void HelpCommand_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e) => App.Instance.OpenHelp();

    private void LogsFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Managers.SettingsManager.LogFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Managers.SettingsManager.LogFolder}\"") { UseShellExecute = true });
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        App.Instance.ExitApplication();
    }

    private void RestorePosition()
    {
        var s = Settings;
        Width = Math.Max(MinWidth, s.WindowWidth);
        Height = Math.Max(MinHeight, s.WindowHeight);
        if (s.WindowLeft is not double savedLeft || s.WindowTop is not double savedTop) return;
        var rect = new Rect(savedLeft, savedTop, Width, Height);
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!rect.IntersectsWith(screen)) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = savedLeft;
        Top = savedTop;
    }

    private void SavePosition()
    {
        var s = Settings;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.IsEmpty) return;
        s.WindowLeft = bounds.Left;
        s.WindowTop = bounds.Top;
        s.WindowWidth = bounds.Width;
        s.WindowHeight = bounds.Height;
        App.Instance.SaveSettings();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SavePosition();
        if (!App.Instance.IsExiting)
        {
            e.Cancel = true;
            if (Settings.CloseToTray)
                Hide();
            else
                App.Instance.ExitApplication();
        }
        base.OnClosing(e);
    }
}
