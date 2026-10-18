using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using TrayBAK.Managers;
using TrayBAK.Models;

namespace TrayBAK.Services;

public sealed class LogService
{
    private const int MaxInMemory = 5000;
    private const string TimeFormat = "MM-dd-yyyy HH:mm:ss";
    private const string Separator = " | ";
    private readonly object _fileGate = new();

    private static readonly JsonSerializerOptions LegacyOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public string LogPath => Path.Combine(SettingsManager.LogFolder, "traybak.log");

    public event Action<LogEntry>? EntryAdded;

    public static string Format(LogEntry entry)
    {
        var job = string.IsNullOrEmpty(entry.Job) ? "-" : entry.Job.Replace(Separator, " / ");
        var message = entry.Message.Replace("\r", " ").Replace("\n", " ");
        return string.Join(Separator,
            entry.Time.ToString(TimeFormat, CultureInfo.InvariantCulture),
            entry.Level.ToString().ToUpperInvariant().PadRight(7),
            job.PadRight(16),
            message);
    }

    public static LogEntry? Parse(string line)
    {
        if (line.StartsWith('{'))
        {
            try { return JsonSerializer.Deserialize<LogEntry>(line, LegacyOptions); }
            catch (JsonException) { return null; }
        }

        var parts = line.Split(Separator, 4);
        if (parts.Length < 4) return null;
        if (!DateTime.TryParseExact(parts[0].Trim(), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return null;
        if (!Enum.TryParse<LogLevel>(parts[1].Trim(), true, out var level)) return null;
        var job = parts[2].Trim();
        return new LogEntry { Time = time, Level = level, Job = job == "-" ? "" : job, Message = parts[3] };
    }

    public void Load(int keepDays)
    {
        Directory.CreateDirectory(SettingsManager.LogFolder);
        var loaded = new List<LogEntry>();
        lock (_fileGate)
        {
            if (File.Exists(LogPath))
            {
                var cutoff = keepDays > 0 ? DateTime.Now.AddDays(-keepDays) : DateTime.MinValue;
                foreach (var line in File.ReadLines(LogPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var entry = Parse(line);
                    if (entry != null && entry.Time >= cutoff) loaded.Add(entry);
                }
                File.WriteAllLines(LogPath, loaded.Select(Format));
            }
        }
        foreach (var entry in loaded.Skip(Math.Max(0, loaded.Count - MaxInMemory)))
            Entries.Add(entry);
    }

    public void Add(LogLevel level, string job, string message)
    {
        var entry = new LogEntry { Time = DateTime.Now, Level = level, Job = job, Message = message };
        lock (_fileGate)
        {
            try
            {
                Directory.CreateDirectory(SettingsManager.LogFolder);
                File.AppendAllText(LogPath, Format(entry) + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        dispatcher.BeginInvoke(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > MaxInMemory) Entries.RemoveAt(0);
            EntryAdded?.Invoke(entry);
        });
    }

    public void Clear()
    {
        lock (_fileGate)
        {
            try { File.WriteAllText(LogPath, ""); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Entries.Clear();
    }
}
