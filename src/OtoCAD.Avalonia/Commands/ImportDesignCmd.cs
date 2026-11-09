#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using lcdb;
using lcdb.Host;
using lcdb.Optic;
using lcdb.Optic.Import;

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
        catch (Exception ex) { host.SetStatus($"[导入光学设计] 读取失败: {ex.Message}"); return; }

        if (!data.IsValid) { host.SetStatus("[导入光学设计] 解析失败: " + string.Join("; ", data.Errors)); if(host is MainWindow errorOwner)await Slim.SlimDialogs.Message(errorOwner,"设计文件未导入",string.Join("\n",data.Errors)); return; }

        var unsupported=data.Surfaces.Where(s=>!new[]{"STANDARD","EVENASPH","EVENASPHERE","CONIC","SPHERICAL","ASPHERIC",""}.Contains((s.Type??"").ToUpperInvariant())).ToList();
        if(data.Mode!="SEQ"||unsupported.Count>0){host.SetStatus("此设计含当前不支持的非序列或特殊表面："+string.Join(", ",unsupported.Select(s=>s.Type)));return;}
        var result = new PrescriptionToLensBuilder().Build(
            data, new PrescriptionToLensBuilder.Options { SourceLabel = Path.GetFileName(path) });
        if (result.Entities.Count == 0)
        {
            host.SetStatus("[导入光学设计] 未生成元件: " + string.Join("; ", result.Warnings));
            return;
        }

        string fileName = Path.GetFileName(path);
        var reportDir=Path.Combine(AppContext.BaseDirectory,"Data","ImportReports");Directory.CreateDirectory(reportDir);
        File.WriteAllLines(Path.Combine(reportDir,DateTime.Now.ToString("yyyyMMdd_HHmmss")+".txt"),new[]{path}.Concat(data.Warnings).Concat(result.Warnings));
        var materialWarnings = result.Warnings.Where(w => w.StartsWith("材料", StringComparison.Ordinal)).ToList();
        if (materialWarnings.Count > 0 && host is MainWindow warningOwner)
            await Slim.SlimDialogs.Message(warningOwner, "请核对导入材料", string.Join("\n\n", materialWarnings));
        var sel = await host.ShowImportSelectionAsync(result.Entities, $"{fileName} · {result.Entities.Count} 元件"+(data.ExtraData.TryGetValue("CodeVConfiguration",out var cvz)?$" · CODE V Z{cvz}":"")+"；未给口径时选用可编辑示意值。");
        if (sel is null) { host.SetStatus("[导入光学设计] 已取消"); return; }
        var toAdd = sel.Selected;
        if (toAdd.Count == 0) { host.SetStatus("[导入光学设计] 未选择任何元件"); return; }

        if (sel.MakeLayoutTab)
            host.AddLayoutTab($"光路图 - {fileName}", toAdd);

        int failed=0; var failures=new System.Collections.Generic.List<string>();
        if (sel.MakePerElementTabs)
        {
            int n = 0;
            foreach (var e in toAdd)
            {
                n++;
                string label = e switch
                {
                    CementedLens cl => $"{cl.Material1}+{cl.Material2}",
                    OpticalLens ol => ol.MaterialName,
                    _ => "元件"
                };
                try{host.AddElementSheetTab($"元件{n} {label}", e);}
                catch(Exception ex){failed++;failures.Add($"元件{n}: {ex.Message}");}
            }
        }

        if (!sel.MakeLayoutTab && !sel.MakePerElementTabs)
            host.AddToActiveDocument(toAdd);

        int singles = toAdd.Count(x => x is OpticalLens);
        int cemented = toAdd.Count(x => x is CementedLens);
        var msg = $"[导入光学设计] {fileName}: 导入 {singles} 单 + {cemented} 胶合";
        if (result.SkippedCount > 0) msg += $", 跳过 {result.SkippedCount}";
        if (result.Warnings.Count > 0) msg += $" (告警 {result.Warnings.Count})";
        if(failed>0) msg += $"；{failed} 张出图失败："+string.Join("; ",failures);
        host.SetStatus(msg);
        if(failed>0&&host is MainWindow failureOwner)await Slim.SlimDialogs.Message(failureOwner,"部分图纸未生成",string.Join("\n",failures)+"\n\n可重新导入并修改口径，或检查模板零件图区大小。");
        if (sel.MakeLayoutTab || failed < toAdd.Count) onImported?.Invoke();
    }
}

