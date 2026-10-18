using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TrayBAK.Models;

namespace TrayBAK.Windows;

public partial class AlertPopupWindow : Window
{
    private static readonly List<AlertPopupWindow> OpenAlerts = new();
    private readonly DispatcherTimer _timer;

    private AlertPopupWindow(string title, string message, LogLevel level)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        var key = level switch
        {
            LogLevel.Success => "SuccessBrush",
            LogLevel.Error => "ErrorBrush",
            LogLevel.Warning => "WarnBrush",
            _ => "RunningBrush"
        };
        Bar.Background = (Brush)Application.Current.FindResource(key);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(level == LogLevel.Error ? 15 : 8) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Close();
        };
    }

    public static void ShowAlert(string title, string message, LogLevel level)
    {
        var window = new AlertPopupWindow(title, message, level);
        OpenAlerts.Add(window);
        window.Show();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Reposition();
        _timer.Start();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _timer.Stop();
        OpenAlerts.Remove(this);
        Reposition();
    }

    private void Window_Click(object sender, MouseButtonEventArgs e)
    {
        Close();
    }

    private static void Reposition()
    {
        var area = SystemParameters.WorkArea;
        var bottom = area.Bottom - 12;
        foreach (var window in OpenAlerts.AsEnumerable().Reverse())
        {
            if (!window.IsLoaded) continue;
            window.Left = area.Right - window.ActualWidth - 12;
            window.Top = bottom - window.ActualHeight;
            bottom = window.Top - 8;
        }
    }
}
