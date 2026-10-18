using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrayBAK.Models;

namespace TrayBAK.Managers;

public static class SettingsManager
{
    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DataFolder { get; } = Path.Combine(AppContext.BaseDirectory, "config");

    public static string LogFolder { get; } = Path.Combine(AppContext.BaseDirectory, "logs");

    public static bool EnsureWritable(out string error)
    {
        try
        {
            foreach (var folder in new[] { DataFolder, LogFolder })
            {
                Directory.CreateDirectory(folder);
                var probe = Path.Combine(folder, ".write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
            }
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string SettingsPath => Path.Combine(DataFolder, "settings.json");

    public static AppSettings Load()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                try { File.Copy(SettingsPath, SettingsPath + ".bad", true); } catch (IOException) { }
            }
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(DataFolder);
            var temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, SettingsPath, true);
        }
    }
}
