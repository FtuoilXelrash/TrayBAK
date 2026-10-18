using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TrayBAK.Models;

namespace TrayBAK.Windows;

public partial class JobEditorWindow : Window
{
    private readonly BackupJob _job;
    private readonly List<string> _otherNames;
    private readonly (CheckBox Box, DayOfWeek Day)[] _days;
    private List<string> _excludedPaths;
    private string _pathsSource;

    public BackupJob Result => _job;

    public JobEditorWindow(BackupJob job, IEnumerable<string> otherNames, bool isNew)
    {
        InitializeComponent();
        _job = job.Clone();
        _otherNames = otherNames.ToList();
        _days = new[]
        {
            (DayMon, DayOfWeek.Monday), (DayTue, DayOfWeek.Tuesday), (DayWed, DayOfWeek.Wednesday),
            (DayThu, DayOfWeek.Thursday), (DayFri, DayOfWeek.Friday), (DaySat, DayOfWeek.Saturday),
            (DaySun, DayOfWeek.Sunday)
        };

        _excludedPaths = new List<string>(_job.ExcludedPaths);
        _pathsSource = _job.SourceFolder;
        UpdateSelectionText();

        Title = isNew ? "New Backup Job" : "Edit Backup Job";
        NameBox.Text = _job.Name;
        SourceBox.Text = _job.SourceFolder;
        DestBox.Text = _job.DestinationFolder;
        EnabledBox.IsChecked = _job.Enabled;
        ExcludeBox.Text = string.Join(Environment.NewLine, _job.Excludes);
        ScheduleBox.SelectedIndex = (int)_job.Schedule;
        IntervalBox.Text = _job.IntervalMinutes.ToString();
        MinuteBox.Text = _job.MinutePastHour.ToString();
        TimeBox.Text = _job.GetTimeOfDay().ToString(@"hh\:mm");
        foreach (var (box, day) in _days) box.IsChecked = _job.Days.Contains(day);
        RunMissedBox.IsChecked = _job.RunMissed;
        KeepDaysBox.Text = _job.KeepDays.ToString();
        KeepCountBox.Text = _job.KeepCount.ToString();
        AlertSuccessBox.IsChecked = _job.AlertOnSuccess;
        AlertFailureBox.IsChecked = _job.AlertOnFailure;
        UpdateSchedulePanels();
    }

    private void Schedule_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateSchedulePanels();
    }

    private void UpdateSchedulePanels()
    {
        if (IntervalPanel == null) return;
        var type = (ScheduleType)Math.Max(0, ScheduleBox.SelectedIndex);
        IntervalPanel.Visibility = type == ScheduleType.Minutes ? Visibility.Visible : Visibility.Collapsed;
        HourlyPanel.Visibility = type == ScheduleType.Hourly ? Visibility.Visible : Visibility.Collapsed;
        TimePanel.Visibility = type is ScheduleType.Daily or ScheduleType.Weekly ? Visibility.Visible : Visibility.Collapsed;
        DaysPanel.Visibility = type == ScheduleType.Weekly ? Visibility.Visible : Visibility.Collapsed;
        RunMissedBox.Visibility = type == ScheduleType.Manual ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFolder("Select the folder to back up", SourceBox.Text);
        if (path != null) SourceBox.Text = path;
    }

    private void BrowseDest_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFolder("Select where backups are stored", DestBox.Text);
        if (path != null) DestBox.Text = path;
    }

    private static string SameFolder(string path)
    {
        try { return Path.GetFullPath(path.Trim()).TrimEnd('\\', '/'); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path.Trim(); }
    }

    private void UpdateSelectionText()
    {
        SelectionText.Text = _excludedPaths.Count == 0
            ? "Everything in the source folder is backed up."
            : $"Everything in the source folder is backed up except {_excludedPaths.Count} unticked file or folder item(s).";
        ResetSelectionButton.IsEnabled = _excludedPaths.Count > 0;
    }

    private void ChooseItems_Click(object sender, RoutedEventArgs e)
    {
        var source = SourceBox.Text.Trim();
        if (source.Length == 0 || !Directory.Exists(source))
        {
            Warn("Choose an existing source folder first.");
            SourceBox.Focus();
            return;
        }

        if (_excludedPaths.Count > 0 && !string.Equals(SameFolder(source), SameFolder(_pathsSource), StringComparison.OrdinalIgnoreCase))
            _excludedPaths.Clear();

        var dialog = new FolderPickerWindow(source, _excludedPaths) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _excludedPaths = dialog.ExcludedPaths;
        _pathsSource = source;
        UpdateSelectionText();
    }

    private void ResetSelection_Click(object sender, RoutedEventArgs e)
    {
        _excludedPaths.Clear();
        UpdateSelectionText();
    }

    private string? PickFolder(string title, string current)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current)) dialog.InitialDirectory = current;
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0) { Warn("Enter a name for the job."); NameBox.Focus(); return; }
        if (_otherNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            Warn("Another job already uses that name. Job names must be unique because they are used in the backup file names.");
            NameBox.Focus();
            return;
        }

        var source = SourceBox.Text.Trim();
        var dest = DestBox.Text.Trim();
        if (source.Length == 0 || !Directory.Exists(source)) { Warn("The source folder does not exist."); SourceBox.Focus(); return; }
        if (dest.Length == 0) { Warn("Choose a backup folder."); DestBox.Focus(); return; }
        if (string.Equals(Path.GetFullPath(source).TrimEnd('\\'), Path.GetFullPath(dest).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            Warn("The backup folder cannot be the same as the source folder.");
            DestBox.Focus();
            return;
        }

        var type = (ScheduleType)Math.Max(0, ScheduleBox.SelectedIndex);
        var interval = _job.IntervalMinutes;
        var minute = _job.MinutePastHour;
        var time = _job.TimeOfDay;
        var days = new List<DayOfWeek>();

        if (type == ScheduleType.Minutes)
        {
            if (!int.TryParse(IntervalBox.Text, out interval) || interval < 1 || interval > 10080)
            { Warn("Minutes must be a number from 1 to 10080."); IntervalBox.Focus(); return; }
        }
        if (type == ScheduleType.Hourly)
        {
            if (!int.TryParse(MinuteBox.Text, out minute) || minute < 0 || minute > 59)
            { Warn("Minute must be a number from 0 to 59."); MinuteBox.Focus(); return; }
        }
        if (type is ScheduleType.Daily or ScheduleType.Weekly)
        {
            if (!TimeSpan.TryParseExact(TimeBox.Text.Trim(), new[] { @"h\:mm", @"hh\:mm" }, null, out var t)
                || t < TimeSpan.Zero || t >= TimeSpan.FromHours(24))
            { Warn("Enter the time as HH:mm, for example 12:00."); TimeBox.Focus(); return; }
            time = t.ToString(@"hh\:mm");
        }
        if (type == ScheduleType.Weekly)
        {
            days = _days.Where(d => d.Box.IsChecked == true).Select(d => d.Day).ToList();
            if (days.Count == 0) { Warn("Choose at least one day of the week."); return; }
        }

        if (!int.TryParse(KeepDaysBox.Text, out var keepDays) || keepDays < 0)
        { Warn("Days to keep must be 0 or more."); KeepDaysBox.Focus(); return; }
        if (!int.TryParse(KeepCountBox.Text, out var keepCount) || keepCount < 0)
        { Warn("Number of backups to keep must be 0 or more."); KeepCountBox.Focus(); return; }

        _job.Name = name;
        _job.SourceFolder = source;
        _job.DestinationFolder = dest;
        _job.Enabled = EnabledBox.IsChecked == true;
        if (!string.Equals(SameFolder(source), SameFolder(_pathsSource), StringComparison.OrdinalIgnoreCase))
            _excludedPaths.Clear();
        _job.ExcludedPaths = new List<string>(_excludedPaths);
        _job.Excludes = ExcludeBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _job.Schedule = type;
        _job.IntervalMinutes = interval;
        _job.MinutePastHour = minute;
        _job.TimeOfDay = time;
        _job.Days = days;
        _job.RunMissed = RunMissedBox.IsChecked == true;
        _job.KeepDays = keepDays;
        _job.KeepCount = keepCount;
        _job.AlertOnSuccess = AlertSuccessBox.IsChecked == true;
        _job.AlertOnFailure = AlertFailureBox.IsChecked == true;
        _job.Anchor = DateTime.Now;

        DialogResult = true;
    }

    private void Warn(string message)
    {
        MessageBox.Show(this, message, "TrayBAK", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
