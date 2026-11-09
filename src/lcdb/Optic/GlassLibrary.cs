using System.Collections.Generic;
using System.Linq;

namespace lcdb.Optic;

/// <summary>
/// 常用光学玻璃静态库 (Schott / CDGM 常用牌号).
/// 用户也可选 "Custom" 自填 n/V.
///
/// n_d = 587.6 nm (氦黄 d 线) 处折射率
/// V_d = (n_d - 1) / (n_F - n_C), 阿贝数 (色散)
/// </summary>
public static class GlassLibrary
{
    public record Glass(string Name, double Nd, double Vd, string Vendor = "Schott");

    private static readonly IReadOnlyList<Glass> BuiltIns = new[]
    {
        // ===== Schott (主流冠/火石玻璃, ~14 种) =====
        new Glass("N-BK7",   1.5168, 64.17),   // 最常用冠玻璃
        new Glass("BK7",     1.5168, 64.17),   // 兼容旧记法
        new Glass("N-K5",    1.5224, 59.48),
        new Glass("N-KF9",   1.5230, 51.50),
        new Glass("N-BAK1",  1.5725, 57.55),
        new Glass("N-BAK4",  1.5688, 56.13),
        new Glass("N-PSK53A",1.6180, 63.39),
        new Glass("F2",      1.6200, 36.37),   // 火石玻璃 (双胶合常用)
        new Glass("F5",      1.6034, 38.03),
        new Glass("N-SF2",   1.6477, 33.85),
        new Glass("N-SF5",   1.6727, 32.25),
        new Glass("N-SF8",   1.6889, 31.18),
        new Glass("N-SF10",  1.7283, 28.53),
        new Glass("N-SF11",  1.7847, 25.76),   // 高折射高色散
        new Glass("N-SF66",  1.9229, 20.88),   // 超高折射
        new Glass("N-LAK8",  1.7130, 53.83),
        new Glass("N-LAK22", 1.6510, 55.89),   // 高折射低色散
        new Glass("N-LASF44",1.8042, 39.58),
        new Glass("N-LASF46A",1.9037,31.32),
        new Glass("N-FK51A", 1.4866, 84.47),   // 萤石替代 (ED 玻璃)

        // ===== CDGM 成都光明 (国产替代, ~7 种) =====
        new Glass("H-K9L",   1.5168, 64.17, "CDGM"),    // N-BK7 国产对应
        new Glass("H-K50",   1.5187, 60.41, "CDGM"),
        new Glass("H-ZF6",   1.7552, 27.51, "CDGM"),
        new Glass("H-ZF52",  1.8467, 23.79, "CDGM"),    // 高折火石
        new Glass("H-LAK51",1.6968, 55.46,  "CDGM"),
        new Glass("H-LAF50A",1.7725, 49.62,  "CDGM"),
        new Glass("H-QK1",   1.4760, 65.86,  "CDGM"),   // 冕牌

        // ===== Ohara (日本, ~5 种) =====
        new Glass("S-BSL7",  1.5168, 64.20, "Ohara"),
        new Glass("S-TIH53", 1.8467, 23.79, "Ohara"),
        new Glass("S-LAH64", 1.7880, 47.49, "Ohara"),
        new Glass("S-FPL51", 1.4970, 81.61, "Ohara"),
        new Glass("S-NPH1",  1.8081, 22.76, "Ohara"),

        // ===== Hoya (日本, ~5 种) =====
        new Glass("BSC7",    1.5168, 64.17, "Hoya"),
        new Glass("F8",      1.5953, 39.19, "Hoya"),
        new Glass("FF5",     1.5927, 35.45, "Hoya"),
        new Glass("LAC14",   1.6968, 55.46, "Hoya"),
        new Glass("FCD1",    1.4970, 81.61, "Hoya"),    // 萤石

        // ===== 特种 (非氧化物 / 单晶) =====
        new Glass("Fused Silica", 1.4585, 67.82, "—"),  // 熔融石英
        new Glass("Sapphire",     1.7682, 72.20, "—"),  // 蓝宝石
        new Glass("CaF2",         1.4338, 95.10, "—"),  // 氟化钙
        new Glass("ZnSe",         2.4028, 12.78, "—"),  // 红外
    };

    private static readonly System.Lazy<IReadOnlyList<Glass>> Catalogs=new(LoadCatalogs);
    public static IReadOnlyList<Glass> All=>Catalogs.Value.Concat(BuiltIns).GroupBy(g=>(g.Name.ToUpperInvariant(),g.Vendor.ToUpperInvariant())).Select(g=>g.First()).ToList();
    public static List<string> CatalogDiagnostics{get;}=new();
    private static IReadOnlyList<Glass> LoadCatalogs()
    {
        var list=new List<Glass>();string dir=System.IO.Path.Combine(System.AppContext.BaseDirectory,"Config","GlassCatalogs");if(!System.IO.Directory.Exists(dir))return list;
        foreach(string path in System.IO.Directory.GetFiles(dir).Where(p=>p.EndsWith(".agf",System.StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p))
        {
            string vendor=System.IO.Path.GetFileNameWithoutExtension(path).ToUpperInvariant();if(vendor.StartsWith("CDGM"))vendor="CDGM";else if(vendor.StartsWith("SCHOTT"))vendor="Schott";
            try
            {
                foreach(string line in System.IO.File.ReadLines(path))
                {
                    var t=line.Trim().Split((char[]?)null,System.StringSplitOptions.RemoveEmptyEntries);if(t.Length<6||!t[0].Equals("NM",System.StringComparison.OrdinalIgnoreCase))continue;
                    if(double.TryParse(t[4],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double nd)&&double.TryParse(t[5],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double vd)&&double.IsFinite(nd)&&nd>1&&double.IsFinite(vd)&&vd>0)list.Add(new Glass(t[1],nd,vd,vendor));
                }
            }
            catch(System.Exception ex){CatalogDiagnostics.Add(System.IO.Path.GetFileName(path)+": "+ex.Message);}
        }
        return list;
    }
    private static string Key(string s)=>new string(s.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    /// <summary>Vendor suffix constrains the catalog. Only spelling punctuation is normalized; grade suffixes remain distinct.</summary>
    public static Glass? Find(string? name)
    {
        if(string.IsNullOrWhiteSpace(name))return null;name=name.Trim();string? vendor=null;
        foreach(string v in new[]{"CDGM","SCHOTT","OHARA","HOYA"})if(name.EndsWith("_"+v,System.StringComparison.OrdinalIgnoreCase)){vendor=v;name=name.Substring(0,name.Length-v.Length-1);break;}
        string key=Key(name);
        return Catalogs.Value.Concat(BuiltIns).FirstOrDefault(g=>(vendor is null||g.Vendor.Equals(vendor,System.StringComparison.OrdinalIgnoreCase))&&Key(g.Name)==key);
    }
    /// <summary>Respect recognized vendor names in GCAT; never cross a known vendor boundary silently.</summary>
    public static Glass? FindForCatalogs(string? name, string? sourceCatalogs)
    {
        if (string.IsNullOrWhiteSpace(sourceCatalogs)) return Find(name);
        var vendors = sourceCatalogs.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(c => new[] { "CDGM", "SCHOTT", "OHARA", "HOYA" }.FirstOrDefault(v => c.StartsWith(v, System.StringComparison.OrdinalIgnoreCase)))
            .Where(v => v != null).Distinct().ToList();
        if (vendors.Count == 0) return Find(name);
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        if (new[] { "CDGM", "SCHOTT", "OHARA", "HOYA" }.Any(v => name.EndsWith("_" + v, System.StringComparison.OrdinalIgnoreCase)))
        {
            var candidate = Find(name);
            return candidate != null && vendors.Any(v => candidate.Vendor.Equals(v, System.StringComparison.OrdinalIgnoreCase)) ? candidate : null;
        }
        foreach (string vendor in vendors)
        {
            var candidate = Find(name + "_" + vendor);
            if (candidate != null) return candidate;
        }
        return null;
    }
    public static Glass Default=>BuiltIns[0];
}
