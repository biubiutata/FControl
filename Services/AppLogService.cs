using System.Collections.ObjectModel;

namespace FControl.Services;

public sealed class AppLogService
{
    private readonly object _gate = new();
    private readonly List<string> _lines = [];

    public event EventHandler<string>? LineAdded;
    public bool IsEnabled { get; set; }

    public void Info(string message)
    {
        Add("INFO", message);
    }

    public void Warn(string message)
    {
        Add("WARN", message);
    }

    public void Error(string message)
    {
        Add("ERROR", message);
    }

    public IReadOnlyList<string> GetSnapshot()
    {
        lock (_gate)
        {
            return _lines.ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
        }
    }

    public void Export(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, GetSnapshot());
    }

    private void Add(string level, string message)
    {
        if (!IsEnabled)
        {
            return;
        }

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (_gate)
        {
            _lines.Add(line);
            if (_lines.Count > 2000)
            {
                _lines.RemoveRange(0, _lines.Count - 2000);
            }
        }

        LineAdded?.Invoke(this, line);
    }
}
