using System.Diagnostics;
using System.IO;
using System.Windows;
using TrayBAK.Managers;
using TrayBAK.Models;

namespace TrayBAK.Windows;

public partial class OptionsWindow : Window
{
    private readonly AppSettings _settings;

    public OptionsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        bool registered;
        try { registered = StartupManager.IsEnabled(); } catch (Exception) { registered = settings.StartWithWindows; }
        StartWithWindowsBox.IsChecked = registered;
        StartMinimizedBox.IsChecked = settings.StartMinimized;
        CloseToTrayBox.IsChecked = settings.CloseToTray;
        ThemeBox.SelectedIndex = settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        PopupBox.IsChecked = settings.PopupAlerts;
        SoundBox.IsChecked = settings.SoundAlerts;
        LogDaysBox.Text = settings.LogKeepDays.ToString();
        RetryBox.IsChecked = settings.RetryFailedBackups;
        RetryCountBox.Text = settings.RetryCount.ToString();
        RetryDelayBox.Text = settings.RetryDelayMinutes.ToString();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(LogDaysBox.Text, out var days) || days < 0)
        {
            MessageBox.Show(this, "Log days must be 0 or more.", "TrayBAK", MessageBoxButton.OK, MessageBoxImage.Warning);
            LogDaysBox.Focus();
            return;
        }

        if (!int.TryParse(RetryCountBox.Text, out var retryCount) || retryCount < 1 || retryCount > 10)
        {
            MessageBox.Show(this, "Retries must be a number from 1 to 10.", "TrayBAK", MessageBoxButton.OK, MessageBoxImage.Warning);
            RetryCountBox.Focus();
            return;
        }
        if (!int.TryParse(RetryDelayBox.Text, out var retryDelay) || retryDelay < 1 || retryDelay > 1440)
        {
            MessageBox.Show(this, "Minutes between tries must be a number from 1 to 1440.", "TrayBAK", MessageBoxButton.OK, MessageBoxImage.Warning);
            RetryDelayBox.Focus();
            return;
        }

        var startWithWindows = StartWithWindowsBox.IsChecked == true;
        try
        {
            StartupManager.Set(startWithWindows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not change the Windows startup setting: " + ex.Message, "TrayBAK",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            startWithWindows = _settings.StartWithWindows;
        }

        _settings.StartWithWindows = startWithWindows;
        _settings.StartMinimized = StartMinimizedBox.IsChecked == true;
        _settings.CloseToTray = CloseToTrayBox.IsChecked == true;
        _settings.Theme = ThemeBox.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
        _settings.PopupAlerts = PopupBox.IsChecked == true;
        _settings.SoundAlerts = SoundBox.IsChecked == true;
        _settings.LogKeepDays = days;
        _settings.RetryFailedBackups = RetryBox.IsChecked == true;
        _settings.RetryCount = retryCount;
        _settings.RetryDelayMinutes = retryDelay;

        App.Instance.SaveSettings();
        App.Instance.ApplyTheme();
        DialogResult = true;
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SettingsManager.LogFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SettingsManager.LogFolder}\"") { UseShellExecute = true });
    }
}
