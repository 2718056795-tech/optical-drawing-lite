using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Threading;
using lcdb.IO;

namespace OtoCAD.Avalonia.Services;

/// <summary>Each document has a stable recovery file in a process-owned session directory.</summary>
public sealed class AutoSaveService : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly string SessionId = Guid.NewGuid().ToString("N");
    private static FileStream? _sessionLock;
    private static readonly object SessionGate = new();
    private readonly DispatcherTimer _timer;
    private readonly Func<bool> _isDirty;
    private readonly Func<string?> _currentFilePath;
    private readonly Action<string> _save;
    private readonly Action<string> _onStatusReport;
    private readonly Func<string>? _documentName;
    public string AutoSaveDir { get; }
    public string BackupPath { get; }
    public static string DefaultDir => AppDataPaths.AutoSaveDirectory;

    public AutoSaveService(Func<bool> isDirty, Func<string?> currentFilePath,
        Action<string> save, Action<string> onStatusReport, Func<string>? documentName = null)
    {
        _isDirty = isDirty;
        _currentFilePath = currentFilePath;
        _save = save;
        _onStatusReport = onStatusReport;
        _documentName = documentName;
        AutoSaveDir = Path.Combine(DefaultDir, SessionId);
        Directory.CreateDirectory(AutoSaveDir);
        lock (SessionGate)
            _sessionLock ??= new FileStream(Path.Combine(AutoSaveDir, "session.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        BackupPath = Path.Combine(AutoSaveDir, Guid.NewGuid().ToString("N") + ".autosave.json");
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => BackupNow();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
    public void Dispose() => Stop();

    public bool BackupNow()
    {
        if (!_isDirty()) return false;
        try
        {
            _save(BackupPath);
            var name = _currentFilePath() is { } path ? Path.GetFileName(path) : _documentName?.Invoke() ?? "未命名图纸";
            AtomicFile.Write(BackupPath + ".meta.json", stream =>
                JsonSerializer.Serialize(stream, new BackupInfo(name, _currentFilePath())));
            _onStatusReport("已自动备份：" + name);
            return true;
        }
        catch (Exception ex)
        {
            _onStatusReport("自动备份失败：" + ex.Message);
            return false;
        }
    }

    public void Discard() => DeleteBackup(BackupPath);

    public static string GetDisplayName(string path)
    {
        try
        {
            var meta = JsonSerializer.Deserialize<BackupInfo>(File.ReadAllText(path + ".meta.json"));
            if (!string.IsNullOrWhiteSpace(meta?.Name)) return meta.Name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return Path.GetFileName(path);
    }

    public static IReadOnlyList<string> FindBackups()
    {
        var backups = new List<string>();
        if (!Directory.Exists(DefaultDir)) return backups;
        // Legacy backups have no session directory. They remain available after upgrade.
        backups.AddRange(Directory.GetFiles(DefaultDir, "*.autosave.json"));
        foreach (var directory in Directory.GetDirectories(DefaultDir))
        {
            try
            {
                using var claim = new FileStream(Path.Combine(directory, "session.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                backups.AddRange(Directory.GetFiles(directory, "*.autosave.json"));
            }
            catch (IOException) { } // Another running instance owns this session.
            catch (UnauthorizedAccessException) { }
        }
        return backups.OrderByDescending(File.GetLastWriteTimeUtc).ToList();
    }

    public static void DeleteBackup(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetFullPath(DefaultDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !fullPath.EndsWith(".autosave.json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("恢复文件路径无效。", nameof(path));
        foreach (string suffix in new[] { "", ".bak", ".meta.json", ".meta.json.bak" })
        {
            try { if (File.Exists(fullPath + suffix)) File.Delete(fullPath + suffix); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed record BackupInfo(string Name, string? OriginalPath);
}
