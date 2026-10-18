using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using TrayBAK.Models;

namespace TrayBAK.Services;

public sealed class BackupResult
{
    public bool Success { get; set; }
    public int Files { get; set; }
    public int Skipped { get; set; }
    public long Bytes { get; set; }
    public TimeSpan Duration { get; set; }
    public int Removed { get; set; }
    public string Message { get; set; } = "";
    public string? ZipPath { get; set; }
    public string? LogPath { get; set; }
    public List<string> SkippedFiles { get; } = new();
}

public static class BackupService
{
    private const string DateFormat = "MM-dd-yyyy";
    private const string StampFormat = "MM-dd-yyyy_HH-mm-ss";
    private const long MaxJobLogBytes = 1024 * 1024;
    private static readonly object StampGate = new();
    private static readonly Dictionary<string, DateTime> LastStamps = new();

    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var safe = new string(chars).Trim('.', ' ');
        return safe.Length == 0 ? "Backup" : safe;
    }

    public static string JobFolder(BackupJob job)
    {
        return Path.Combine(job.DestinationFolder, SafeName(job.Name));
    }

    public static string FolderToOpen(BackupJob job)
    {
        var folder = JobFolder(job);
        return Directory.Exists(folder) ? folder : job.DestinationFolder;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var i = 0;
        while (size >= 1024 && i < units.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return i == 0 ? $"{bytes} B" : $"{size:N1} {units[i]}";
    }

    public static Task<BackupResult> RunAsync(BackupJob job, CancellationToken ct)
    {
        var snapshot = job.Clone();
        return Task.Run(() => Run(snapshot, ct), ct);
    }

    private static BackupResult Run(BackupJob job, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = new BackupResult();
        string? temp = null;
        try
        {
            if (string.IsNullOrWhiteSpace(job.SourceFolder) || !Directory.Exists(job.SourceFolder))
                return Fail(result, sw, "Source folder not found: " + job.SourceFolder);
            if (string.IsNullOrWhiteSpace(job.DestinationFolder))
                return Fail(result, sw, "No destination folder set");

            Directory.CreateDirectory(job.DestinationFolder);

            var source = Path.GetFullPath(job.SourceFolder).TrimEnd('\\', '/');
            var destination = Path.GetFullPath(job.DestinationFolder).TrimEnd('\\', '/');
            var safe = SafeName(job.Name);
            var jobFolder = Path.Combine(destination, safe);
            Directory.CreateDirectory(jobFolder);
            var final = NextBackupPath(jobFolder, safe);
            temp = final + ".tmp";
            var prefix = Path.GetFileName(source);
            if (prefix.Length == 0) prefix = "root";
            var excludes = job.Excludes.Where(e => !string.IsNullOrWhiteSpace(e)).Select(BuildMatcher).ToList();
            var excludedPaths = new HashSet<string>(
                job.ExcludedPaths.Select(p => p.Replace('\\', '/').Trim('/')), StringComparer.OrdinalIgnoreCase);

            using (var zipStream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var pending = new Stack<string>();
                pending.Push(source);
                while (pending.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var dir = pending.Pop();
                    var rel = Path.GetRelativePath(source, dir).Replace('\\', '/');
                    if (rel != ".")
                        archive.CreateEntry(prefix + "/" + rel + "/");

                    IEnumerable<string> subDirs;
                    IEnumerable<string> files;
                    try
                    {
                        subDirs = Directory.EnumerateDirectories(dir).ToList();
                        files = Directory.EnumerateFiles(dir).ToList();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        result.Skipped++;
                        result.SkippedFiles.Add(dir + " (folder)");
                        continue;
                    }

                    foreach (var sub in subDirs)
                    {
                        if (string.Equals(Path.GetFullPath(sub).TrimEnd('\\'), destination, StringComparison.OrdinalIgnoreCase)) continue;
                        var subRel = Path.GetRelativePath(source, sub).Replace('\\', '/');
                        if (excludedPaths.Contains(subRel) || IsExcluded(excludes, Path.GetFileName(sub), subRel)) continue;
                        if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;
                        pending.Push(sub);
                    }

                    foreach (var file in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        var fileRel = Path.GetRelativePath(source, file).Replace('\\', '/');
                        if (excludedPaths.Contains(fileRel) || IsExcluded(excludes, Path.GetFileName(file), fileRel)) continue;
                        try
                        {
                            using var input = new FileStream(file, FileMode.Open, FileAccess.Read,
                                FileShare.ReadWrite | FileShare.Delete);
                            var entry = archive.CreateEntry(prefix + "/" + fileRel, CompressionLevel.Optimal);
                            entry.LastWriteTime = ClampTime(File.GetLastWriteTime(file));
                            using var output = entry.Open();
                            input.CopyTo(output);
                            result.Files++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            result.Skipped++;
                            result.SkippedFiles.Add(file);
                        }
                    }
                }
            }

            File.Move(temp, final, true);
            temp = null;
            result.ZipPath = final;
            result.Bytes = new FileInfo(final).Length;
            result.Success = true;
            result.Removed = ApplyRetention(job, jobFolder, safe, final);
            result.Message = $"{result.Files} files, {FormatSize(result.Bytes)}"
                + (result.Skipped > 0 ? $", {result.Skipped} skipped" : "")
                + (result.Removed > 0 ? $", {result.Removed} old removed" : "");
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.Message = "Cancelled";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = ex.Message;
        }
        finally
        {
            if (temp != null)
            {
                try { File.Delete(temp); } catch (IOException) { }
            }
            sw.Stop();
            result.Duration = sw.Elapsed;
            result.LogPath = AppendJobLog(job, result);
        }
        return result;
    }

    private static string? AppendJobLog(BackupJob job, BackupResult result)
    {
        if (string.IsNullOrWhiteSpace(job.DestinationFolder) || !Directory.Exists(job.DestinationFolder)) return null;

        var logPath = Path.Combine(job.DestinationFolder, SafeName(job.Name) + ".log");
        var status = result.Success ? "OK" : result.Message == "Cancelled" ? "CANCELLED" : "FAILED";
        var detail = result.Success
            ? $"{Path.GetFileName(result.ZipPath)}  {result.Message}, {result.Duration.TotalSeconds:N1} s"
            : result.Message;
        var lines = new List<string> { $"{DateTime.Now:MM-dd-yyyy HH:mm:ss}  {status,-9}  {detail}" };
        foreach (var skipped in result.SkippedFiles.Take(20)) lines.Add("    skipped: " + skipped);
        if (result.SkippedFiles.Count > 20) lines.Add($"    ... and {result.SkippedFiles.Count - 20} more skipped");

        try
        {
            var info = new FileInfo(logPath);
            if (info.Exists && info.Length > MaxJobLogBytes)
            {
                var all = File.ReadAllLines(logPath);
                File.WriteAllLines(logPath, all.Skip(all.Length / 2));
            }
            File.AppendAllLines(logPath, lines);
            return logPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static BackupResult Fail(BackupResult result, Stopwatch sw, string message)
    {
        sw.Stop();
        result.Success = false;
        result.Message = message;
        result.Duration = sw.Elapsed;
        return result;
    }

    private static DateTimeOffset ClampTime(DateTime t)
    {
        if (t.Year < 1980) return new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        if (t.Year > 2107) return new DateTimeOffset(2107, 12, 31, 0, 0, 0, TimeSpan.Zero);
        return new DateTimeOffset(t);
    }

    private static string NextBackupPath(string destination, string safe)
    {
        var key = Path.Combine(destination, safe).ToLowerInvariant();
        var now = DateTime.Now;
        var stampTime = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond);
        lock (StampGate)
        {
            if (LastStamps.TryGetValue(key, out var last) && stampTime <= last) stampTime = last.AddSeconds(1);
            var path = StampPath(destination, safe, stampTime);
            while (File.Exists(path))
            {
                stampTime = stampTime.AddSeconds(1);
                path = StampPath(destination, safe, stampTime);
            }
            LastStamps[key] = stampTime;
            return path;
        }
    }

    private static string StampPath(string destination, string safe, DateTime stampTime)
    {
        return Path.Combine(destination, $"{safe}_{stampTime.ToString(StampFormat, CultureInfo.InvariantCulture)}.zip");
    }

    private static Regex BuildMatcher(string pattern)
    {
        var p = pattern.Trim().Replace('\\', '/').Trim('/');
        var regex = "^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static bool IsExcluded(List<Regex> matchers, string name, string relPath)
    {
        foreach (var m in matchers)
        {
            if (m.ToString().Contains('/') ? m.IsMatch(relPath) : m.IsMatch(name)) return true;
        }
        return false;
    }

    public static int ApplyRetention(BackupJob job, string destination, string safe, string keepPath)
    {
        if (job.KeepDays <= 0 && job.KeepCount <= 0) return 0;

        var stamped = new Regex("^" + Regex.Escape(safe) + @"_(\d{2}-\d{2}-\d{4}_\d{2}-\d{2}-\d{2})(?:_(\d+))?\.zip$", RegexOptions.IgnoreCase);
        var legacy = new Regex("^" + Regex.Escape(safe) + @"_(\d{2}-\d{2}-\d{4})\.(\d+)\.zip$", RegexOptions.IgnoreCase);
        var backups = new List<(string Path, DateTime Date, int Seq)>();
        foreach (var file in Directory.EnumerateFiles(destination, safe + "_*.zip"))
        {
            var name = Path.GetFileName(file);
            var m = stamped.Match(name);
            if (m.Success)
            {
                if (!DateTime.TryParseExact(m.Groups[1].Value, StampFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var stampDate)) continue;
                backups.Add((file, stampDate, m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1));
                continue;
            }
            m = legacy.Match(name);
            if (!m.Success) continue;
            if (!DateTime.TryParseExact(m.Groups[1].Value, DateFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date)) continue;
            backups.Add((file, date, int.Parse(m.Groups[2].Value)));
        }

        var ordered = backups.OrderByDescending(b => b.Date).ThenByDescending(b => b.Seq).ToList();
        var removed = 0;
        var cutoff = DateTime.Today.AddDays(-job.KeepDays);
        for (var i = 0; i < ordered.Count; i++)
        {
            var b = ordered[i];
            if (string.Equals(b.Path, keepPath, StringComparison.OrdinalIgnoreCase)) continue;
            var tooMany = job.KeepCount > 0 && i >= job.KeepCount;
            var tooOld = job.KeepDays > 0 && b.Date.Date <= cutoff;
            if (!tooMany && !tooOld) continue;
            try
            {
                File.Delete(b.Path);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return removed;
    }
}
