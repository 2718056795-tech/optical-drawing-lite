using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace lcdb.Optic.Import
{
    /// <summary>
    /// Zemax .zmx 文件解析器
    /// 独立解析zmx文本格式，不依赖ZOSAPI
    /// </summary>
    public class ZmxParser
    {
        #region Constants

        private static readonly HashSet<string> KnownSurfaceTypes = new HashSet<string>
        {
            "STANDARD", "EVENASPH", "ODDASPHE", "BINARY2", "TOROIDAL",
            "COORDBRK", "PARAXIAL", "DGRATING", "GRINSUR", "TILTSURF"
        };

        #endregion

        #region Public Methods

        /// <summary>
        /// 解析.zmx文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>解析结果</returns>
        public ZmxSystemData Parse(string filePath)
        {
            if (!File.Exists(filePath))
            {
                var result = new ZmxSystemData { FilePath = filePath };
                result.AddError($"文件不存在: {filePath}");
                return result;
            }

            try
            {
                // Zemax文件可能使用不同编码，尝试多种编码
                string content = ReadFileWithEncoding(filePath);
                var data = ParseContent(content);
                data.FilePath = filePath;
                return data;
            }
            catch (Exception ex)
            {
                var result = new ZmxSystemData { FilePath = filePath };
                result.AddError($"读取文件失败: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// 解析zmx文件内容
        /// </summary>
        /// <param name="content">文件内容</param>
        /// <returns>解析结果</returns>
        public ZmxSystemData ParseContent(string content)
        {
            var data = new ZmxSystemData();

            if (string.IsNullOrWhiteSpace(content))
            {
                data.AddError("文件内容为空");
                return data;
            }

            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            ZmxSurface currentSurface = null;
            int surfaceCount = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    // 检测表面定义开始
                    if (line.StartsWith("SURF"))
                    {
                        // 保存前一个表面
                        if (currentSurface != null)
                        {
                            data.Surfaces.Add(currentSurface);
                        }

                        // 创建新表面
                        currentSurface = new ZmxSurface();
                        var match = Regex.Match(line, @"SURF\s+(\d+)");
                        if (match.Success)
                        {
                            currentSurface.SurfaceNumber = int.Parse(match.Groups[1].Value);
                        }
                        else
                        {
                            currentSurface.SurfaceNumber = surfaceCount;
                        }
                        surfaceCount++;
                        continue;
                    }

                    // 解析系统级参数
                    if (ParseSystemParameter(line, data)) continue;

                    // 解析表面参数
                    if (currentSurface != null)
                    {
                        ParseSurfaceParameter(line, currentSurface, data, i + 1);
                    }
                }
                catch (Exception ex)
                {
                    string location = currentSurface == null ? "系统" : $"表面 S{currentSurface.SurfaceNumber}";
                    data.AddError($"行 {i + 1}，{location} 解析失败: {line} ({ex.Message})");
                }
            }

            // 添加最后一个表面
            if (currentSurface != null)
            {
                data.Surfaces.Add(currentSurface);
            }

            // Normalize only after all records are collected.
            NormalizeUnits(data);
            foreach (var surface in data.Surfaces)
                if (data.ExtraData.TryGetValue("ZemaxGlassCatalogs", out var catalogs)) surface.ExtraParameters["sourceCatalogs"] = catalogs;
            // 后处理
            PostProcess(data);

            return data;
        }

        /// <summary>
        /// 验证文件格式
        /// </summary>
        public bool ValidateFormat(string filePath, out List<string> errors)
        {
            errors = new List<string>();

            if (!File.Exists(filePath))
            {
                errors.Add($"文件不存在: {filePath}");
                return false;
            }

            try
            {
                string content = ReadFileWithEncoding(filePath);

                // 检查是否包含基本的zmx结构
                if (!content.Contains("SURF"))
                {
                    errors.Add("文件不包含表面定义 (SURF)");
                    return false;
                }

                // 检查是否为序列模式
                if (content.Contains("MODE NSC"))
                {
                    errors.Add("不支持非序列模式 (NSC)，仅支持序列模式");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errors.Add($"验证失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 尝试多种编码读取文件
        /// </summary>
        private string ReadFileWithEncoding(string filePath)
        {
            // 尝试UTF-8
            try
            {
                string content = File.ReadAllText(filePath, Encoding.UTF8);
                if (IsValidZmxContent(content)) return content;
            }
            catch { }

            // 尝试ANSI (Windows默认)
            try
            {
                string content = File.ReadAllText(filePath, Encoding.Default);
                if (IsValidZmxContent(content)) return content;
            }
            catch { }

            // 尝试UTF-16
            try
            {
                string content = File.ReadAllText(filePath, Encoding.Unicode);
                if (IsValidZmxContent(content)) return content;
            }
            catch { }

            // 最后尝试ASCII
            return File.ReadAllText(filePath, Encoding.ASCII);
        }

        /// <summary>
        /// 检查内容是否为有效的zmx格式
        /// </summary>
        private bool IsValidZmxContent(string content)
        {
            return content.Contains("SURF") || content.Contains("VERS") || content.Contains("MODE");
        }

        /// <summary>
        /// 解析系统级参数
        /// </summary>
        private bool ParseSystemParameter(string line, ZmxSystemData data)
        {
            var fields = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var key = fields[0].ToUpperInvariant();
            if (key == "UNIT")
            {
                if (fields.Length < 2) throw new FormatException("UNIT 缺少长度单位");
                string unit = fields[1].ToUpperInvariant();
                if (data.ExtraData.TryGetValue("ZemaxLengthUnit", out var previous) && (string)previous != unit)
                    throw new FormatException("文件中存在冲突的长度单位");
                data.ExtraData["ZemaxLengthUnit"] = unit; return true;
            }
            if (key == "GCAT") { data.ExtraData["ZemaxGlassCatalogs"] = string.Join(" ", fields.Skip(1)); return true; }
            if (key == "MCON" || key == "MNUM" && (fields.Length < 2 || !int.TryParse(fields[1], out int configCount) || configCount > 1))
            {
                data.AddError("检测到 Zemax 多配置数据，当前静态导入不能选取配置；请在 Zemax 中将所需配置另存为单配置文件后导入。"); return true;
            }
            // 版本
            if (line.StartsWith("VERS"))
            {
                data.Version = line.Substring(4).Trim();
                return true;
            }

            // 模式
            if (line.StartsWith("MODE"))
            {
                data.Mode = line.Substring(4).Trim();
                return true;
            }

            // 标题
            if (line.StartsWith("NAME") || line.StartsWith("TITL"))
            {
                data.Title = line.Substring(4).Trim().Trim('"');
                return true;
            }

            // 波长
            if (line.StartsWith("WAVM"))
            {
                var match = Regex.Match(line, @"WAVM\s+\d+\s+([\d.E+-]+)");
                if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double wavelength))
                {
                    data.Wavelengths.Add(wavelength);
                }
                return true;
            }

            // 系统孔径
            if (key == "ENPD")
            {
                if (fields.Length < 2) throw new FormatException("ENPD 缺少值");
                data.SystemAperture = ParseFiniteNumber(fields[1], key);
                data.ApertureType = "EPD";
                return true;
            }

            if (key == "FLOA")
            {
                if (fields.Length < 2) throw new FormatException("FLOA 缺少值");
                data.SystemAperture = ParseFiniteNumber(fields[1], key);
                data.ApertureType = "F/#";
                return true;
            }

            return false;
        }

        /// <summary>
        /// 解析表面参数
        /// </summary>
        private void ParseSurfaceParameter(string line, ZmxSurface surface, ZmxSystemData data, int lineNumber)
        {
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1 && parts[0].Equals("STOP", StringComparison.OrdinalIgnoreCase)) { surface.IsStop = true; return; }
            if (parts.Length == 0) return;
            string key = parts[0].ToUpperInvariant();
            surface.ExtraParameters["sourceLine:" + key] = lineNumber;
            if (parts.Length < 2)
            {
                if (new[] { "TYPE", "CURV", "DISZ", "GLAS", "DIAM", "MEMA", "CLAP", "FLAP", "CONI", "PARM" }.Contains(key))
                    throw new FormatException(key + " 缺少值");
                return;
            }
            string value = parts.Length > 1 ? parts[1] : "";

            switch (key)
            {
                case "TYPE":
                    surface.Type = value;
                    if (!KnownSurfaceTypes.Contains(value))
                    {
                        data.AddWarning($"表面 {surface.SurfaceNumber}: 未知表面类型 '{value}'");
                    }
                    break;

                case "CURV":
                    double curv = ParseFiniteNumber(value, key);
                    surface.Curvature = curv;
                    surface.Radius = curv == 0 ? double.PositiveInfinity : 1.0 / curv;
                    if (curv != 0 && !double.IsFinite(surface.Radius)) throw new FormatException("CURV 的倒数超出曲率半径有效数值范围");
                    break;

                case "DISZ":
                    if (value.Equals("INFINITY", StringComparison.OrdinalIgnoreCase))
                    {
                        surface.Thickness = double.PositiveInfinity;
                    }
                    else
                    {
                        surface.Thickness = ParseFiniteNumber(value, key);
                    }
                    break;

                case "GLAS":
                    surface.Glass = value;
                    surface.ExtraParameters["sourceGlasRecord"] = line;
                    if (parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int glassSolve))
                        surface.ZemaxGlassSolveType = glassSolve;
                    if (parts.Length > 4 && double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double nd) && double.IsFinite(nd) && nd > 1)
                        surface.RefractiveIndexPost = nd;
                    if (parts.Length > 5 && double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double vd) && double.IsFinite(vd) && vd > 0)
                        surface.AbbeNumberPost = vd;
                    break;

                case "DIAM":
                    surface.SemiDiameter = ParseAperture(value, key);
                    break;

                // 机械半口径 (Mechanical Semi-Diameter): MEMA <value> ... ; > DIAM 时净口径外有平肩.
                case "MEMA":
                    surface.MechanicalSemiDiameter = ParseAperture(value, key);
                    break;

                // 圆形通光裁剪: CLAP(circular) / FLAP(floating, 自动随光束). 格式 <r_min> <r_max>, r_max=裁剪半径.
                // (真实 Zemax 样本里 FLAP 远多于 CLAP; 二者语义对 2D 净口径都取 r_max.)
                case "CLAP":
                case "FLAP":
                    if (parts.Length < 3) throw new FormatException(key + " 缺少内外半径");
                    double inner = ParseAperture(value, key);
                    double outer = ParseAperture(parts[2], key);
                    if (inner > outer) throw new FormatException(key + " 内半径不能大于外半径");
                    surface.ClearSemiDiameter = outer;
                    if (inner > 0) data.AddWarning($"表面 S{surface.SurfaceNumber}: {key} 内半径 {inner} 未绘制，当前仅采用外通光半径。");
                    break;

                case "CONI":
                    surface.Conic = ParseFiniteNumber(value, key);
                    break;

                case "COMM":
                    surface.Comment = string.Join(" ", parts.Skip(1));
                    break;

                case "STOP":
                    surface.IsStop = true;
                    break;

                // 非球面系数
                case "PARM":
                    ParseAsphericParameter(parts, surface);
                    break;
            }

        }

        private static double ParseFiniteNumber(string value, string field)
        {
            if (!double.TryParse(value.Replace('D', 'E').Replace('d', 'e'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                throw new FormatException(field + " 必须为有限数值，实际为 " + value);
            return number;
        }

        private static double ParseAperture(string value, string field)
        {
            double number = ParseFiniteNumber(value, field);
            if (number < 0) throw new FormatException(field + " 半径不能为负数");
            return number;
        }

        // Zemax EVENASPH: PARM 1 is A2, PARM 2 is A4, ... PARM 8 is A16.
        // Internal/legacy arrays remain A4-first; CODE V's A coefficient remains A4.
        private void ParseAsphericParameter(string[] parts, ZmxSurface surface)
        {
            if (parts.Length < 3 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index < 1)
                throw new FormatException("非球面参数无效");
            double value = ParseFiniteNumber(parts[2], "PARM " + index);
            if (surface.Type != "EVENASPH") return;
            if (index == 1) surface.QuadraticCoefficient = value;
            else if (index >= 2 && index <= 8) surface.AsphericCoefficients[index - 2] = value;
            else if (value != 0) throw new FormatException("EVENASPH 仅支持参数 1 至 8");
        }

        private static void NormalizeUnits(ZmxSystemData data)
        {
            if (!data.ExtraData.TryGetValue("ZemaxLengthUnit", out var raw))
            { data.AddError("Zemax 文件缺少 UNIT 长度单位，不能猜测毫米；请重新导出带单位的文件。"); return; }
            string unit = (string)raw;
            double factor = unit switch { "MM" => 1, "CM" => 10, "M" => 1000, "IN" or "INCH" or "INCHES" => 25.4, _ => double.NaN };
            if (!double.IsFinite(factor)) { data.AddError("不支持的 Zemax 长度单位：" + unit); return; }
            data.ExtraData["LengthUnit"] = "mm";
            data.ExtraData["SourceLengthScaleToMm"] = factor;
            foreach (var surface in data.Surfaces)
            {
                surface.Radius = ScaleLength(surface.Radius, factor, "CURV", surface, data, true);
                surface.Thickness = ScaleLength(surface.Thickness, factor, "DISZ", surface, data, true);
                surface.Curvature /= factor;
                surface.SemiDiameter = ScaleLength(surface.SemiDiameter, factor, "DIAM", surface, data);
                if (surface.ClearSemiDiameter.HasValue)
                    surface.ClearSemiDiameter = ScaleLength(surface.ClearSemiDiameter.Value, factor,
                        surface.ExtraParameters.ContainsKey("sourceLine:FLAP") ? "FLAP" : "CLAP", surface, data);
                if (surface.MechanicalSemiDiameter.HasValue)
                    surface.MechanicalSemiDiameter = ScaleLength(surface.MechanicalSemiDiameter.Value, factor, "MEMA", surface, data);
                surface.QuadraticCoefficient /= factor;
                for (int i = 0; i < surface.AsphericCoefficients.Length; i++) surface.AsphericCoefficients[i] /= Math.Pow(factor, 2 * i + 3);
            }
            if (data.ApertureType == "EPD")
            {
                data.SystemAperture *= factor;
                if (!double.IsFinite(data.SystemAperture)) data.AddError("系统 ENPD 换算为毫米后超出有效数值范围。");
            }
            if (factor != 1) data.AddWarning($"长度单位 {unit} 已转换为 mm，非球面系数同步换算。");
        }

        private static double ScaleLength(double value, double factor, string field, ZmxSurface surface,
            ZmxSystemData data, bool allowExplicitInfinity = false)
        {
            if (allowExplicitInfinity && double.IsInfinity(value)) return value;
            double scaled = value * factor;
            if (!double.IsFinite(scaled))
            {
                string line = surface.ExtraParameters.TryGetValue("sourceLine:" + field, out var sourceLine)
                    ? $"行 {sourceLine}，" : "";
                data.AddError($"{line}表面 S{surface.SurfaceNumber}: {field} 换算为毫米后超出有效数值范围。");
            }
            return scaled;
        }

        /// <summary>
        /// 后处理 - 补充和验证数据
        /// </summary>
        private void PostProcess(ZmxSystemData data)
        {
            // 验证表面数量
            if (data.Surfaces.Count < 2)
            {
                data.AddError("表面数量不足 (至少需要OBJ和IMG)");
                return;
            }

            foreach (var surface in data.Surfaces.Skip(1).SkipLast(1).Where(s => s.HasMaterial))
            {
                if (!double.IsFinite(surface.Thickness) || surface.Thickness <= 0)
                {
                    string line = surface.ExtraParameters.TryGetValue("sourceLine:DISZ", out var sourceLine)
                        ? $"行 {sourceLine}，" : "";
                    data.AddError($"{line}表面 S{surface.SurfaceNumber}: 玻璃中心厚度必须为正的有限数值，请检查 DISZ。");
                }
            }

            // 检查是否有光学元件
            bool hasElement = false;
            for (int i = 1; i < data.Surfaces.Count - 1; i++)
            {
                if (data.Surfaces[i].HasMaterial)
                {
                    hasElement = true;
                    break;
                }
            }

            if (!hasElement)
            {
                data.AddWarning("未检测到光学元件 (所有表面都是空气间隔)");
            }

            // 设置默认波长
            if (data.Wavelengths.Count == 0)
            {
                data.Wavelengths.Add(0.55); // 默认可见光波长 (um)
                data.AddWarning("未指定波长，使用默认值 0.55um");
            }

            // 验证元件数据
            foreach (var element in data.Elements)
            {
                if (element.CenterThickness <= 0)
                {
                    data.AddWarning($"元件 ({element.GlassMaterial}): 中心厚度为零或负数");
                }
                if (element.Diameter <= 0)
                {
                    data.AddWarning($"元件 ({element.GlassMaterial}): 直径为零或负数");
                }
            }
        }

        #endregion
    }
}
