using System.Collections.ObjectModel;
using System.IO;

namespace TrayBAK.ViewModels;

public sealed class FileNode : ViewModelBase
{
    private readonly string _source;
    private readonly HashSet<string> _saved;
    private bool? _state = true;
    private bool _loaded;

    private FileNode(string source, string fullPath, bool isDirectory, FileNode? parent,
        HashSet<string> saved, bool? state, bool placeholder)
    {
        _source = source;
        _saved = saved;
        _state = state;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        IsPlaceholder = placeholder;
        Parent = parent;
        Name = placeholder ? "Loading..." : (parent == null ? fullPath : Path.GetFileName(fullPath));
        RelPath = parent == null ? "." : Path.GetRelativePath(source, fullPath).Replace('\\', '/');
        if (isDirectory && !placeholder)
            Children.Add(new FileNode(source, fullPath, false, this, saved, true, true));
    }

    public string Name { get; }
    public string FullPath { get; }
    public string RelPath { get; }
    public bool IsDirectory { get; }
    public bool IsPlaceholder { get; }
    public FileNode? Parent { get; }
    public ObservableCollection<FileNode> Children { get; } = new();
    public bool IsLoaded => _loaded;

    public bool? IsChecked
    {
        get => _state;
        set
        {
            var v = value ?? false;
            if (value == false && _state == null) v = true;
            Apply(v);
        }
    }

    public static FileNode CreateRoot(string source, IEnumerable<string> excluded)
    {
        var saved = new HashSet<string>(excluded.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        return new FileNode(source, source, true, null, saved, true, false);
    }

    public static string Normalize(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    public void SetAll(bool value)
    {
        Apply(value);
    }

    public void EnsureLoaded()
    {
        if (_loaded || !IsDirectory || IsPlaceholder) return;
        _loaded = true;
        Children.Clear();

        var dirs = new List<string>();
        var files = new List<string>();
        try
        {
            dirs.AddRange(Directory.EnumerateDirectories(FullPath));
            files.AddRange(Directory.EnumerateFiles(FullPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        dirs.Sort(StringComparer.OrdinalIgnoreCase);
        files.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in dirs)
        {
            try
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            Children.Add(Create(dir, true));
        }
        foreach (var file in files) Children.Add(Create(file, false));

        RecomputeFromChildren();
    }

    public List<string> CollectExcluded()
    {
        var result = new List<string>();
        Collect(result);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private FileNode Create(string path, bool isDirectory)
    {
        var rel = Path.GetRelativePath(_source, path).Replace('\\', '/');
        return new FileNode(_source, path, isDirectory, this, _saved, Initial(rel, isDirectory), false);
    }

    private bool? Initial(string rel, bool isDirectory)
    {
        if (_state == false) return false;
        if (_saved.Contains(rel)) return false;
        if (isDirectory && _saved.Any(s => s.StartsWith(rel + "/", StringComparison.OrdinalIgnoreCase))) return null;
        return true;
    }

    private void Apply(bool value)
    {
        if (Parent == null)
            _saved.Clear();
        else
            _saved.RemoveWhere(s => string.Equals(s, RelPath, StringComparison.OrdinalIgnoreCase)
                || s.StartsWith(RelPath + "/", StringComparison.OrdinalIgnoreCase));
        SetDown(value);
        Parent?.RecomputeFromChildren();
    }

    private void SetDown(bool? value)
    {
        _state = value;
        Raise(nameof(IsChecked));
        if (!_loaded) return;
        foreach (var child in Children)
            if (!child.IsPlaceholder) child.SetDown(value);
    }

    private void RecomputeFromChildren()
    {
        if (!_loaded) return;
        var real = Children.Where(c => !c.IsPlaceholder).ToList();
        if (real.Count == 0) return;
        bool? state = real.All(c => c._state == true) ? true
            : real.All(c => c._state == false) ? false
            : null;
        if (_state != state)
        {
            _state = state;
            Raise(nameof(IsChecked));
        }
        Parent?.RecomputeFromChildren();
    }

    private void Collect(List<string> result)
    {
        if (Parent != null)
        {
            if (_state == false)
            {
                result.Add(RelPath);
                return;
            }
            if (_state == true) return;
        }

        if (_loaded)
        {
            foreach (var child in Children)
                if (!child.IsPlaceholder) child.Collect(result);
        }
        else
        {
            result.AddRange(_saved.Where(s => s.StartsWith(RelPath + "/", StringComparison.OrdinalIgnoreCase)));
        }
    }
}
