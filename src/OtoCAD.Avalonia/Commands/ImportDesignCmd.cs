#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using lcdb;
using lcdb.Host;
using lcdb.Optic;
using lcdb.Optic.Import;
using OtoCAD.Avalonia.Services;

namespace OtoCAD.Avalonia.Commands;

/// <summary>
/// 光学设计导入 (Optiland/Zemax JSON 或 .zmx) → OpticalLens / CementedLens。
///
/// v0.5.0 起属免费内核: 解析引擎在 lcdb.Optic.Import, 本类只做 UI 流程 (选文件 → 解析 →
/// 元件选择 → 落到光路图/逐元件加工图/当前文档)。此前它是付费模块 OtoCAD.Cloud 经
/// IAppHost.RegisterCommand 反射注入的, 免费版没有这条命令 ——
/// 于是"光学设计 → 加工图"这条主链对开源用户是断的。
///
/// 仍走 <see cref="IAppHost"/> 而非直接依赖 MainWindow: 保持 UI 交互与流程解耦, 也让
/// 这段逻辑与原付费模块实现保持一致 (搬迁而非重写)。
/// </summary>
public static class ImportDesignCmd
{
    public static Task RunAsync(IAppHost host) => RunAsync(host, null);

    internal static async Task RunAsync(IAppHost host, Action? onImported)
    {
        var path = await host.PickFileAsync("导入设计 (Zemax / CODE V / Optic JSON)", "*.zmx", "*.seq", "*.json");
        if (path is null) { host.SetStatus("[导入光学设计] 已取消"); return; }

        ZmxSystemData data;
        try
        {
            if(string.Equals(Path.GetExtension(path),".seq",StringComparison.OrdinalIgnoreCase))
            {
                var parser=new CodeVSeqParser();data=parser.Parse(path);
                if(parser.ConfigurationCount>1&&host is MainWindow configOwner)
                {
                    var answer=await Slim.SlimDialogs.Prompt(configOwner,"选择 CODE V 配置","1",$"文件含 {parser.ConfigurationCount} 个配置。输入要读取的配置编号（1—{parser.ConfigurationCount}）。只读镜头数据，不执行宏。");
                    if(answer is null)return;
                    if(!int.TryParse(answer,out int z)||z<1||z>parser.ConfigurationCount){host.SetStatus("配置编号无效，请重新导入。");return;}
                    data=parser.Parse(path,z);
                }
            }
            else if (string.Equals(Path.GetExtension(path), ".zmx", StringComparison.OrdinalIgnoreCase))
                data = new ZmxParser().Parse(path);
            else
                data = new OpticJsonDeserializer().Parse(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            await ShowImportNoticeAsync(host, "设计文件未导入", "读取失败：" + ex.Message);
            return;
        }

        if (!data.IsValid) { host.SetStatus("[导入光学设计] 解析失败: " + string.Join("; ", data.Errors)); if(host is MainWindow errorOwner)await Slim.SlimDialogs.Message(errorOwner,"设计文件未导入",string.Join("\n",data.Errors)); return; }

        var unsupported=data.Surfaces.Where(s=>!new[]{"STANDARD","EVENASPH","EVENASPHERE","CONIC","SPHERICAL","ASPHERIC",""}.Contains((s.Type??"").ToUpperInvariant())).ToList();
        if (!string.Equals(data.Mode, "SEQ", StringComparison.OrdinalIgnoreCase) || unsupported.Count > 0)
        {
            string reason = "当前支持序列单透镜和双胶合透镜。";
            if (!string.Equals(data.Mode, "SEQ", StringComparison.OrdinalIgnoreCase)) reason += "\n文件模式：" + data.Mode;
            if (unsupported.Count > 0) reason += "\n不支持的表面：\n" + string.Join("\n", unsupported.Select(s => $"S{s.SurfaceNumber} · {s.Type}"));
            await ShowImportNoticeAsync(host, "设计文件未导入", reason);
            return;
        }

        PrescriptionToLensBuilder.Result result;
        try
        {
            result = new PrescriptionToLensBuilder().Build(
                data, new PrescriptionToLensBuilder.Options { SourceLabel = Path.GetFileName(path) });
        }
        catch (Exception ex)
        {
            await ShowImportNoticeAsync(host, "设计文件未导入", "生成元件失败：" + ex.Message);
            return;
        }
        var warnings = data.Warnings.Concat(result.Warnings).Distinct().ToList();
        if (result.Entities.Count == 0)
        {
            await ShowImportNoticeAsync(host, "未生成元件", string.Join("\n\n", warnings));
            return;
        }

        string fileName = Path.GetFileName(path);
        string? reportPath = null;
        try
        {
            string reportDir = AppDataPaths.ImportReportsDirectory;
            Directory.CreateDirectory(reportDir);
            reportPath = Path.Combine(reportDir, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllLines(reportPath, new[] { path, $"已解析 {result.Entities.Count} 个元件，跳过 {result.SkippedCount} 组。" }.Concat(warnings));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            reportPath = null;
            await ShowImportNoticeAsync(host, "导入报告未保存", "报告写入失败，仍可继续导入。\n\n" + ex.Message);
        }
        if (warnings.Count > 0)
        {
            string summary = $"可导入 {result.Entities.Count} 个元件，跳过 {result.SkippedCount} 组。\n\n" + string.Join("\n\n", warnings);
            if (reportPath != null) summary += "\n\n完整报告：" + reportPath;
            await ShowImportNoticeAsync(host, "请核对导入结果", summary);
        }
        IImportSelection? sel;
        try
        {
            sel = await host.ShowImportSelectionAsync(result.Entities, $"{fileName} · {result.Entities.Count} 元件" +
                (data.ExtraData.TryGetValue("CodeVConfiguration", out var cvz) ? $" · CODE V Z{cvz}" : "") +
                "；未给口径时选用可编辑示意值。");
        }
        catch (Exception ex)
        {
            await ShowImportNoticeAsync(host, "设计文件未导入", "导入预览无法打开，请核对处方单位和数值。\n\n" + ex.Message);
            return;
        }
        if (sel is null) { host.SetStatus("[导入光学设计] 已取消"); return; }
        var toAdd = sel.Selected;
        if (toAdd.Count == 0) { host.SetStatus("[导入光学设计] 未选择任何元件"); return; }

        if (host is MainWindow importOwner) importOwner.PrepareDrawingImport(fileName);
        string configuration = ImportIdentity.Configuration(toAdd[0]);
        string layoutTitle = $"光路图 - {fileName}" + (configuration.Length == 0 ? "" : " · " + configuration);
        int failed=0; var failures=new System.Collections.Generic.List<string>();
        bool layoutCreated = false;
        if (sel.MakeLayoutTab)
        {
            try { host.AddLayoutTab(layoutTitle, toAdd); layoutCreated = true; }
            catch (Exception ex) { failures.Add("光路总览：" + ex.Message); }
        }

        if (sel.MakePerElementTabs)
        {
            int n = 0;
            foreach (var e in toAdd)
            {
                n++;
                int originalNumber = ImportIdentity.ElementNumber(e, n);
                string label = e switch
                {
                    CementedLens cl => $"{cl.Material1}+{cl.Material2}",
                    OpticalLens ol => ol.MaterialName,
                    _ => "元件"
                };
                string surfaces = ImportIdentity.Tag(e, "sourceSurfaces");
                string trace = string.Join(" · ", new[] { configuration, surfaces }.Where(value => value.Length > 0));
                string title = $"元件{originalNumber} {label}" + (trace.Length == 0 ? "" : $" [{trace}]");
                try{host.AddElementSheetTab(title, e);}
                catch(Exception ex){failed++;failures.Add($"元件{originalNumber}: {ex.Message}");}
            }
        }

        if (!sel.MakeLayoutTab && !sel.MakePerElementTabs)
            host.AddToActiveDocument(toAdd);

        int singles = toAdd.Count(x => x is OpticalLens);
        int cemented = toAdd.Count(x => x is CementedLens);
        var msg = $"[导入光学设计] {fileName}: 导入 {singles} 单 + {cemented} 胶合";
        if (result.SkippedCount > 0) msg += $", 跳过 {result.SkippedCount}";
        if (warnings.Count > 0) msg += $" (提示 {warnings.Count})";
        if(failures.Count>0) msg += $"；{failures.Count} 张出图失败："+string.Join("; ",failures);
        host.SetStatus(msg);
        if(failures.Count>0&&host is MainWindow failureOwner)await Slim.SlimDialogs.Message(failureOwner,"部分图纸未生成",string.Join("\n",failures)+"\n\n可重新导入并修改口径，或检查模板零件图区大小。");
        if (layoutCreated || failed < toAdd.Count) onImported?.Invoke();
    }

    private static async Task ShowImportNoticeAsync(IAppHost host, string title, string message)
    {
        host.SetStatus(title + "：" + message);
        if (host is MainWindow owner) await Slim.SlimDialogs.Message(owner, title, message);
    }
}

