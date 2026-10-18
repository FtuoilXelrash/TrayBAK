using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TrayBAK.Models;

namespace TrayBAK.Windows;

public partial class LogViewerWindow : Window
{
    private readonly ICollectionView _view;

    public LogViewerWindow()
    {
        InitializeComponent();
        var app = App.Instance;
        _view = CollectionViewSource.GetDefaultView(app.Log.Entries);
        _view.Filter = Matches;
        LogGrid.ItemsSource = _view;

        JobBox.Items.Add("All");
        foreach (var name in app.Settings.Jobs.Select(j => j.Name)) JobBox.Items.Add(name);
        JobBox.SelectedIndex = 0;

        app.Log.EntryAdded += OnEntryAdded;
        Closed += (_, _) => app.Log.EntryAdded -= OnEntryAdded;
        UpdateCount();
        ScrollToEnd();
    }

    private bool Matches(object item)
    {
        if (item is not LogEntry entry) return false;
        if (LevelBox.SelectedItem is ComboBoxItem { Content: string level } && level != "All"
            && entry.Level.ToString() != level) return false;
        if (JobBox.SelectedItem is string job && job != "All" && entry.Job != job) return false;
        var text = SearchBox.Text.Trim();
        if (text.Length > 0 && entry.Message.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0
            && entry.Job.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }

    private void OnEntryAdded(LogEntry entry)
    {
        UpdateCount();
        ScrollToEnd();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded && _view == null) return;
        _view?.Refresh();
        UpdateCount();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
        UpdateCount();
    }

    private void UpdateCount()
    {
        if (_view == null) return;
        var shown = _view.Cast<object>().Count();
        CountText.Text = $"{shown} of {App.Instance.Log.Entries.Count} entries";
    }

    private void ScrollToEnd()
    {
        if (LogGrid.Items.Count > 0) LogGrid.ScrollIntoView(LogGrid.Items[^1]);
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var path = App.Instance.Log.LogPath;
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, "Clear the entire log?", "TrayBAK", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        App.Instance.Log.Clear();
        UpdateCount();
    }
}
