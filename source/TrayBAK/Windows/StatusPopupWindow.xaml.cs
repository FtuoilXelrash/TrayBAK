using System.Windows;
using System.Windows.Controls;
using TrayBAK.Managers;
using TrayBAK.Models;
using TrayBAK.Services;
using TrayBAK.ViewModels;

namespace TrayBAK.Windows;

public partial class StatusPopupWindow : Window
{
    private readonly SchedulerService _scheduler;
    private readonly List<JobViewModel> _items;
    private bool _closing;

    public StatusPopupWindow(AppSettings settings, SchedulerService scheduler, TrayState state)
    {
        InitializeComponent();
        Closing += (_, _) => _closing = true;
        _scheduler = scheduler;
        _items = settings.Jobs.Select(j => new JobViewModel(j, scheduler)).ToList();
        Items.ItemsSource = _items;
        HeaderText.Text = "TrayBAK";
        StateText.Text = state switch
        {
            TrayState.Running => "Backup running",
            TrayState.Error => "Last backup failed",
            TrayState.Paused => "Scheduling paused",
            _ => settings.Jobs.Count == 0 ? "No jobs" : "Idle"
        };
        PauseButton.Content = scheduler.IsPaused ? "Resume scheduling" : "Pause scheduling";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 12;
        Top = area.Bottom - ActualHeight - 12;
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is JobViewModel vm)
        {
            _scheduler.RunNow(vm.Job, "manual, tray");
            foreach (var item in _items) item.RaiseAll();
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        Close();
        App.Instance.ShowMainWindow();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _scheduler.SetPaused(!_scheduler.IsPaused);
        Close();
    }
}
