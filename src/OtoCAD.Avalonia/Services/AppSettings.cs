using System;
using System.IO;
using System.Text.Json;

namespace OtoCAD.Avalonia.Services;

/// <summary>
/// 应用设置保存在用户资料目录的 Data/settings.json。
/// </summary>
public sealed class AppSettings
{
    /// <summary>"Light" / "Dark" / "Default" (跟随系统).</summary>
    public string Theme { get; set; } = "Default";

    /// <summary>标注标准: Standard / ISO-25 / GB / GB-Optical / Company.</summary>
    public string DimensionStandard { get; set; } = "GB-Optical";

    /// <summary>出图标准(出图惯例): GB-ISO 10110 / MIL-ANSI / JIS / DIN / 自定义名.</summary>
    public string DrawingConvention { get; set; } = "GB-ISO 10110";

    /// <summary>AutoSave 间隔 (秒). 0 = 关闭. 默认 30.</summary>
    public int AutoSaveSeconds { get; set; } = 30;

    /// <summary>启动时是否加载上次场景 (留 v0.2+).</summary>
    public bool RestoreLastScene { get; set; } = false;

    public string[] ExportFormats { get; set; } = ["dxf", "pdf", "png", "otocad", "json"];

    // ---- 持久化 ----

    private static string StorePath
    {
        get
        {
            return Path.Combine(AppDataPaths.DataDirectory, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new AppSettings();
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        try
        {
            lcdb.IO.AtomicFile.Write(StorePath, stream =>
                JsonSerializer.Serialize(stream, this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 写失败静默 */ }
    }
}
