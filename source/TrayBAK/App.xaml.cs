using System.Diagnostics;
using System.IO;
using System.Windows;
using TrayBAK.Managers;
using TrayBAK.Models;
using TrayBAK.Services;
using TrayBAK.Windows;

namespace TrayBAK;

public partial class App : Application
{
    private const string MutexName = "TrayBAK.SingleInstance.5D2B7E6A-9C41-4C3E-A8E7-0B6F3F1D2A94";
    private const string ShowEventName = "TrayBAK.ShowWindow.5D2B7E6A-9C41-4C3E-A8E7-0B6F3F1D2A94";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private TrayManager? _tray;
    private MainWindow? _mainWindow;
    private AlertService? _alerts;
    private LogViewerWindow? _logWindow;
    private DateTime _lastErrorPopup = DateTime.MinValue;

    public static App Instance => (App)Current;

    public bool IsExiting { get; private set; }

    public AppSettings Settings { get; private set; } = new();
    public LogService Log { get; private set; } = new();
    public SchedulerService Scheduler { get; private set; } = null!;

    public event Action? JobsChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += (_, a) => ReportError("tray", a.Exception);
        DispatcherUnhandledException += (_, a) =>
        {
            a.Handled = true;
            ReportError("window", a.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => ReportError("fatal", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            a.SetObserved();
            ReportError("background", a.Exception);
        };

        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var waiter = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                if (IsExiting) return;
                Dispatcher.BeginInvoke(ShowMainWindow);
            }
        }) { IsBackground = true };
        waiter.Start();

        if (!SettingsManager.EnsureWritable(out var configError))
        {
            MessageBox.Show(
                $"TrayBAK keeps its settings in a config folder next to the program, but that folder cannot be written to:\n\n{SettingsManager.DataFolder}\n\n{configError}\n\nRun TrayBAK from a folder you can write to.",
                "TrayBAK", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        Settings = SettingsManager.Load();
        Log.Load(Settings.LogKeepDays);
        Scheduler = new SchedulerService(Settings, Log);

        ApplyTheme();

        _mainWindow = new MainWindow();
        _tray = new TrayManager(Settings, Scheduler);
        _tray.OpenRequested += ShowMainWindow;
        _tray.ExitRequested += ExitApplication;
        JobsChanged += _tray.Refresh;
        _alerts = new AlertService(Settings, Scheduler);

        Log.Add(LogLevel.Info, "", "TrayBAK started");
        Scheduler.Start();
        if (!Settings.StartMinimized) ShowMainWindow();
    }

    public void ApplyTheme()
    {
        ThemeMode = Settings.Theme switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System
        };
    }

    private void ShowDialog(Window window)
    {
        if (_mainWindow is { IsVisible: true })
            window.Owner = _mainWindow;
        else
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowDialog();
    }

    public void ShowOptions()
    {
        ShowDialog(new OptionsWindow(Settings));
    }

    public void ShowAbout()
    {
        ShowDialog(new AboutWindow());
    }

    public void ShowLog()
    {
        if (_logWindow == null)
        {
            _logWindow = new LogViewerWindow();
            _logWindow.Closed += (_, _) => _logWindow = null;
            if (_mainWindow is { IsVisible: true })
                _logWindow.Owner = _mainWindow;
            else
                _logWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _logWindow.Show();
        }
        _logWindow.Activate();
    }

    public void OpenHelp()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TrayBAK-Help.html");
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        else
            MessageBox.Show("The help file was not found next to the program.", "TrayBAK", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ReportError(string where, Exception? ex)
    {
        var text = ex == null ? "Unknown error" : $"{ex.GetType().Name}: {ex.Message}";
        try
        {
            Log.Add(LogLevel.Error, "", $"Unexpected error ({where}): {text}");
            File.AppendAllText(Path.Combine(SettingsManager.LogFolder, "errors.log"),
                $"{DateTime.Now:MM-dd-yyyy HH:mm:ss}  {where}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            if ((DateTime.Now - _lastErrorPopup).TotalSeconds > 5)
            {
                _lastErrorPopup = DateTime.Now;
                Dispatcher.BeginInvoke(() => AlertPopupWindow.ShowAlert("TrayBAK error", text + "\nDetails are in logs\\errors.log", LogLevel.Error));
            }
        }
        catch (Exception)
        {
        }
    }

    public void SaveSettings()
    {
        try
        {
            SettingsManager.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Add(LogLevel.Error, "", "Could not save settings: " + ex.Message);
        }
    }

    public void NotifyJobsChanged()
    {
        JobsChanged?.Invoke();
    }

    public void ShowMainWindow()
    {
        if (_mainWindow == null) return;
        if (!_mainWindow.IsVisible) _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void ExitApplication()
    {
        if (IsExiting) return;
        if (Scheduler.AnyActive)
        {
            var answer = MessageBox.Show("A backup is running. Exit and cancel it?", "TrayBAK",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            Scheduler.CancelAll();
        }
        IsExiting = true;
        Scheduler.Stop();
        Log.Add(LogLevel.Info, "", "TrayBAK stopped");
        _showEvent?.Set();
        _tray?.Dispose();
        _mainWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
