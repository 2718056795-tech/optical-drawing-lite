using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OtoCAD.Avalonia.Services;

/// <summary>User-owned files survive application updates and work in read-only installations.</summary>
public static class AppDataPaths
{
    private static readonly Lazy<string> Root = new(Initialize);
    public static string RootDirectory => Root.Value;
    public static string DataDirectory => EnsureDirectory(Path.Combine(RootDirectory, "Data"));
    public static string TemplatesDirectory => EnsureDirectory(Path.Combine(RootDirectory, "Templates"));
    public static string AutoSaveDirectory => EnsureDirectory(Path.Combine(DataDirectory, "autosave"));
    public static string ImportReportsDirectory => EnsureDirectory(Path.Combine(DataDirectory, "ImportReports"));
    public static IReadOnlyList<string> MigrationWarnings => Warnings;
    private static readonly List<string> Warnings = new();

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Initialize()
    {
        string appDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        string? configured = Environment.GetEnvironmentVariable("OPTICAL_DRAWING_LITE_DATA_DIR");
        string root = !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            : File.Exists(Path.Combine(appDirectory, "portable.flag"))
                ? appDirectory
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpticalDrawingLite");
        EnsureDirectory(root);
        string marker = Path.Combine(root, ".migration-complete");
        if (!File.Exists(marker))
        {
            // Copy user files before seeding the bundled defaults. Existing destination files win.
            List<string> legacyDirectories;
            try { legacyDirectories = LegacyDirectories(appDirectory).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Warnings.Add("无法检查旧版本目录：" + ex.Message);
                legacyDirectories = new List<string> { appDirectory };
            }
            foreach (string legacy in legacyDirectories)
            {
                if (string.Equals(Path.TrimEndingDirectorySeparator(legacy), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    CopyMissing(Path.Combine(legacy, "Templates"), Path.Combine(root, "Templates"));
                    CopyMissing(Path.Combine(legacy, "Data"), Path.Combine(root, "Data"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Warnings.Add("旧版设置迁移未完成：" + legacy + "；" + ex.Message);
                }
            }
            if (Warnings.Count == 0) File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
        }
        return root;
    }

    private static IEnumerable<string> LegacyDirectories(string appDirectory)
    {
        // In-place upgrades retain the user's original Data and Templates folders.
        if (File.Exists(Path.Combine(appDirectory, "Data", "template-choice.json")) ||
            File.Exists(Path.Combine(appDirectory, "Data", "recent.json")))
            yield return appDirectory;

        var versionDirectory = Directory.GetParent(Path.TrimEndingDirectorySeparator(appDirectory));
        if (versionDirectory is not null && Version.TryParse(versionDirectory.Name.TrimStart('V', 'v'), out var currentVersion) &&
            versionDirectory.Parent is { } releases)
        {
            // Side-by-side V1.x/程序 deployments are used by the existing Windows distribution.
            var previous = releases.EnumerateDirectories()
                .Select(directory => new { Directory = directory, Version = ParseVersion(directory.Name) })
                .Where(item => item.Version is not null && item.Version < currentVersion)
                .OrderByDescending(item => item.Version);
            foreach (var item in previous)
            {
                string candidate = Path.Combine(item.Directory.FullName, "程序");
                if (Directory.Exists(Path.Combine(candidate, "Templates")))
                {
                    yield return candidate;
                    break;
                }
            }
        }
        yield return appDirectory;
    }

    private static Version? ParseVersion(string name) => Version.TryParse(name.TrimStart('V', 'v'), out var version) ? version : null;

    internal static void CreateFileIfMissing(string destination, Action<string> writeCompleteFile)
    {
        if (File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            writeCompleteFile(temporary);
            try { File.Move(temporary, destination); }
            catch (IOException) when (File.Exists(destination)) { }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void CopyMissing(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        string sourcePrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(destination).StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("数据迁移目标不能位于旧数据目录内部。");
        EnsureDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            string target = Path.Combine(destination, Path.GetFileName(file));
            CreateFileIfMissing(target, temporary => File.Copy(file, temporary, overwrite: false));
        }
        foreach (var directory in new DirectoryInfo(source).EnumerateDirectories())
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) == 0)
                CopyMissing(directory.FullName, Path.Combine(destination, directory.Name));
        }
    }
}
