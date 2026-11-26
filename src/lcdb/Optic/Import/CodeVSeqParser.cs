using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace lcdb.Optic.Import;

/// <summary>Read static CODE V LEN prescriptions only. Never executes Macro-PLUS or external commands.</summary>
public sealed class CodeVSeqParser
{
    public int ConfigurationCount{get;private set;}=1;
    public ZmxSystemData Parse(string path,int configuration=1)=>ParseText(File.ReadAllText(path),configuration,path);
    public ZmxSystemData ParseText(string text,int configuration=1,string source="CODE V")
    {
        var data=new ZmxSystemData{FilePath=source,Mode="SEQ"};ConfigurationCount=1;
        var commands=Commands(text).ToList();bool inLens=false,complete=false,imageSeen=false;ZmxSurface? current=null;double unit=1;bool unitSeen=false;
        bool radiusMode = true;
        int ignored=0;
        foreach(string command in commands)
        {
            string[] t=Regex.Matches(command,"\"[^\"]*\"|'[^']*'|[^\\s]+").Select(m=>m.Value.Trim('"','\'')).ToArray();if(t.Length==0)continue;string key=t[0].ToUpperInvariant();
            if (key == "RDM")
            {
                if (t.Length == 1 || (t.Length == 2 && t[1].Equals("YES", StringComparison.OrdinalIgnoreCase))) radiusMode = true;
                else if (t.Length == 2 && t[1].Equals("NO", StringComparison.OrdinalIgnoreCase)) radiusMode = false;
                else data.Errors.Add("RDM 模式无效，须为 YES 或 NO。");
                continue;
            }
            if(!inLens){if(key=="LEN"){inLens=true;}continue;}
            if(key=="GO"){complete=true;break;}
            if(new[]{"IF","FOR","WHI","LCL","GBL","IN","INS","DEL","LEN"}.Contains(key)||command.Contains('^')){data.Errors.Add("包含需执行的宏或结构修改，静态导入不支持："+key);continue;}
            try
            {
                if(key is "SO" or "S" or "SI")
                {
                    if(key=="SI")imageSeen=true;
                    if(t.Length<3)throw new FormatException("表面必须含半径和厚度");double radius = Radius(t[1], radiusMode);
                    current=new ZmxSurface{SurfaceNumber=data.Surfaces.Count,Radius=radius,Curvature=double.IsInfinity(radius)?0:1/radius,Thickness=N(t[2],true),Glass=t.Length>3?t[3]:null};
                    if(current.Glass is "REFL" or "REFRACT" or "MIRROR")data.Errors.Add("当前仅支持折射透镜，不支持反射面。");
                    current.ExtraParameters["codevRow"]=command;data.Surfaces.Add(current);continue;
                }
                if(key=="DIM")
                {
                    if(data.Surfaces.Count>0)throw new FormatException("DIM 必须在表面数据之前");unitSeen=true;unit=t[1].ToUpperInvariant() switch{"M"=>1,"C"=>10,"I"=>25.4,_=>throw new FormatException("不支持的长度单位 "+t[1])};continue;
                }
                if(key is "TITLE" or "TIT"&&t.Length>1&&!Regex.IsMatch(t[1],"^Z\\d+$",RegexOptions.IgnoreCase)){data.Title=string.Join(" ",t.Skip(1));continue;}
                if(key=="WL"){data.Wavelengths=t.Skip(1).Select(x=>N(x)/1000).ToList();continue;}
                if(key=="ZOO")
                {
                    if(t.Length==2&&int.TryParse(t[1],out int count) && count > 0) ConfigurationCount=count;
                    else if (t.Length >= 2 && IsPrescriptionParameter(t[1].ToUpperInvariant()))
                    {
                        if (t.Length < 3 || !Regex.IsMatch(t[2], @"^S\d+$", RegexOptions.IgnoreCase))
                            throw new FormatException("须指定已有表面编号：" + command);
                        int target = int.Parse(t[2].Substring(1), CultureInfo.InvariantCulture);
                        var zoomSurface = data.Surfaces.FirstOrDefault(s => s.SurfaceNumber == target)
                            ?? throw new FormatException("未定义表面 " + t[2]);
                        ApplyValues(zoomSurface, t[1].ToUpperInvariant(), t, 3, configuration, ConfigurationCount);
                    }
                    else if (t.Length >= 2 && t[1].ToUpperInvariant() is "CUX" or "RDX")
                        throw new FormatException("不支持非旋转对称表面 " + t[1]);
                    else if (t.Length < 2 || int.TryParse(t[1], out _)) throw new FormatException("变焦配置数必须为正整数");
                    else if (t[1].EndsWith("DE", StringComparison.OrdinalIgnoreCase)) ignored++;
                    continue;
                }
                if(key=="SLB"&&current is not null){current.Comment=string.Join(" ",t.Skip(1));continue;}
                if(key=="STO"){Target(t,ref current,data,1);if(current is not null)current.IsStop=true;continue;}
                if(key=="CIR")
                {
                    int i=Target(t,ref current,data,1);if(current is null)throw new FormatException("CIR 缺少所属表面");bool edge=i<t.Length&&t[i].Equals("EDG",StringComparison.OrdinalIgnoreCase);if(edge)i++;
                    if(i>=t.Length)throw new FormatException("CIR 缺少半径");double v=N(t[i]);if(v<0)throw new FormatException("CIR 半径不能为负数");if(edge)current.MechanicalSemiDiameter=v;else current.SemiDiameter=v;continue;
                }
                if(IsPrescriptionParameter(key))
                {
                    int i=Target(t,ref current,data,1);if(current is null)throw new FormatException("缺少所属表面");if (i >= t.Length) throw new FormatException("缺少数值");
                    ApplyValues(current, key, t, i, configuration, ConfigurationCount);continue;
                }
                if(key=="ASP"){if(current is not null)current.Type="EVENASPH";continue;}
                if(new[]{"RDX","CUX","HOE","SPS","SPSX","SPSY","TOR","CYL","NSS","UDS","GRI","DOE","XYP","AAS","AOF","QCN","QBF","FRE","NFOG"}.Contains(key)){data.Errors.Add("不支持的特殊面或系统："+key);continue;}
                if(new[]{"XDE","YDE","ZDE","ADE","BDE","CDE","DAR","BEN","RET"}.Contains(key)){ignored++;continue;}
                // Remaining lens setup, solves, weights and analysis settings do not execute.
            }
            catch(Exception ex){data.Errors.Add(key+"："+ex.Message);}
        }
        if(!inLens||data.Surfaces.Count<3)data.Errors.Add("未找到静态 LEN / SO / S / SI 处方；不能执行宏来获得镜头。");
        if(!imageSeen)data.Errors.Add("处方缺少 SI 像面。");
        if(!unitSeen)data.Errors.Add("缺少明确 DIM 单位；请从 CODE V 导出包含单位的处方。");
        if(configuration<1||configuration>ConfigurationCount)data.Errors.Add("配置编号超出范围。");
        foreach(var s in data.Surfaces)
        {
            double Scale(double value, string field, bool allowInfinity = false)
            {
                if (allowInfinity && double.IsInfinity(value)) return value;
                double scaled = value * unit;
                if (!double.IsFinite(scaled)) data.Errors.Add($"表面 S{s.SurfaceNumber}: {field} 换算为毫米后超出有效数值范围。");
                return scaled;
            }
            s.Radius = Scale(s.Radius, "RDY", true);
            s.Thickness = Scale(s.Thickness, "THI", true);
            s.Curvature = double.IsInfinity(s.Radius) ? 0 : 1 / s.Radius;
            s.SemiDiameter = Scale(s.SemiDiameter, "CIR");
            if (s.ClearSemiDiameter.HasValue) s.ClearSemiDiameter = Scale(s.ClearSemiDiameter.Value, "CIR");
            if (s.MechanicalSemiDiameter.HasValue) s.MechanicalSemiDiameter = Scale(s.MechanicalSemiDiameter.Value, "CIR EDG");
            for(int i=0;i<s.AsphericCoefficients.Length;i++)s.AsphericCoefficients[i]/=Math.Pow(unit,2*i+3);
        }
        foreach (var s in data.Surfaces.Skip(1).SkipLast(1).Where(s => s.HasMaterial))
            if (!double.IsFinite(s.Thickness) || s.Thickness <= 0)
                data.Errors.Add($"表面 S{s.SurfaceNumber} 的玻璃中心厚度必须为正的有限数值，请检查表面行或 THI。");
        if(ConfigurationCount>1)data.Warnings.Add($"CODE V 共 {ConfigurationCount} 个配置，读取 Z{configuration}；装调偏心/倾斜不参与单零件加工轮廓。");
        if(ignored>0)data.Warnings.Add("已忽略装配坐标变换；此导入用于零件加工图，不等同完整系统光路。");
        if(complete)data.Warnings.Add("只读取首个 LEN/GO 处方块；后续优化、公差和宏命令均未执行。");
        if(data.Surfaces.Skip(1).SkipLast(1).Any(s=>s.SemiDiameter<=0))data.Warnings.Add("部分表面未提供 CIR 口径；需要补填实际口径，不能从 F/# 或入瞳直接代替。");
        data.ExtraData["CodeVConfiguration"]=configuration;data.ExtraData["CodeVConfigurationCount"]=ConfigurationCount;return data;
    }
    static double N(string text, bool allowInfinity = false)
    {
        if (allowInfinity && (text.Equals("INF", StringComparison.OrdinalIgnoreCase) || text.Equals("INFINITY", StringComparison.OrdinalIgnoreCase)))
            return double.PositiveInfinity;
        if (!double.TryParse(text.Replace('D', 'E').Replace('d', 'e'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            throw new FormatException("必须为有限数值，实际为 " + text);
        return value;
    }
    private static bool IsPrescriptionParameter(string key) =>
        key is "CUY" or "RDY" or "THI" or "GLA" or "CIR" or "K" || (key.Length == 1 && "ABCDEFGH".Contains(key));

    private static double Radius(string text, bool isRadius)
    {
        double value = N(text, isRadius);
        if (value == 0) return double.PositiveInfinity;
        if (isRadius) return value;
        double radius = 1 / value;
        if (!double.IsFinite(radius)) throw new FormatException("曲率倒数超出有效半径范围");
        return radius;
    }

    private static void ApplyValues(ZmxSurface surface, string key, string[] tokens, int first, int configuration, int count)
    {
        bool edge = key == "CIR" && first < tokens.Length && tokens[first].Equals("EDG", StringComparison.OrdinalIgnoreCase);
        if (edge) first++;
        int start = 1, end = count;
        bool qualified = first < tokens.Length && Regex.IsMatch(tokens[first], "^Z[0-9]", RegexOptions.IgnoreCase);
        if (qualified)
        {
            var range = Regex.Match(tokens[first++], @"^Z(\d+)(?:\.\.(\d+))?$", RegexOptions.IgnoreCase);
            if (!range.Success) throw new FormatException("不支持的变焦位置限定符");
            start = int.Parse(range.Groups[1].Value);
            end = range.Groups[2].Success ? int.Parse(range.Groups[2].Value) : start;
            if (start < 1 || end < start || end > count) throw new FormatException("变焦位置超出范围");
        }
        int values = tokens.Length - first;
        if (values == 0) return; // ZOO without values retains the existing value.
        if (values != 1 && values != end - start + 1) throw new FormatException("参数数量与变焦位置数不符");
        // An unqualified scalar entered before ZOO is the base for all subsequent positions.
        if ((qualified || values != 1) && (configuration < start || configuration > end)) return;
        string value = tokens[first + (values == 1 ? 0 : configuration - start)];
        if (value.Equals("N", StringComparison.OrdinalIgnoreCase)) return;
        if (edge)
        {
            double semi = N(value);
            if (semi < 0) throw new FormatException("CIR EDG 半径不能为负数");
            surface.MechanicalSemiDiameter = semi;
        }
        else Apply(surface, key, value);
    }
    static void Apply(ZmxSurface s,string key,string value)
    {
        if(key=="GLA"){s.Glass=value;return;}
        if(key is "CUY" or "RDY"){s.Radius=Radius(value,key=="RDY");return;}
        double v=N(value,key=="THI");
        if (key == "CIR" && v < 0) throw new FormatException("CIR 半径不能为负数");
        switch(key){case "THI":s.Thickness=v;break;case "CIR":s.SemiDiameter=v;break;case "K":s.Conic=v;s.Type="EVENASPH";break;default:int i="ABCDEFGH".IndexOf(key,StringComparison.Ordinal);if(i>=0){s.AsphericCoefficients[i]=v;s.Type="EVENASPH";}break;}
    }
    static int Target(string[] t,ref ZmxSurface? s,ZmxSystemData d,int i)
    {
        if(i<t.Length&&Regex.IsMatch(t[i],"^S\\d+$",RegexOptions.IgnoreCase)){int n=int.Parse(t[i].Substring(1));s=d.Surfaces.Single(x=>x.SurfaceNumber==n);return i+1;}return i;
    }
    static IEnumerable<string> Commands(string text)
    {
        var buffer=new StringBuilder();char quote='\0';bool commentMode=false;
        foreach(char c in text.Replace("\r", ""))
        {
            if(commentMode){if(c!='\n')continue;commentMode=false;}
            if(quote=='\0'&&c=='!'){commentMode=true;continue;}
            if(quote!='\0'){buffer.Append(c);if(c==quote)quote='\0';continue;}
            if(c=='\''||c=='"'){quote=c;buffer.Append(c);continue;}
            if(c==';'||c=='\n')
            {
                string s=buffer.ToString().Trim();if(s.EndsWith("&")){buffer.Clear();buffer.Append(s.Substring(0,s.Length-1)).Append(' ');continue;}
                buffer.Clear();int comment=s.IndexOf('!');if(comment>=0)s=s.Substring(0,comment).Trim();if(s.Length>0)yield return s;continue;
            }
            buffer.Append(c);
        }
        if(buffer.Length>0)yield return buffer.ToString().Trim();
    }
}
