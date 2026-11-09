using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia;
using Avalonia.Threading;
using OtoCAD.Avalonia.Ribbon;
namespace OtoCAD.Avalonia;
public partial class MainWindow
{
    private string _validationOutput="";
    internal void ValidateSlimUI(List<string> log,string output)
    {
        _validationOutput=output;
        var cfg=System.Text.Json.JsonSerializer.Deserialize<RibbonLayoutConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Config","RibbonLayout.json")),new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        foreach(var b in cfg.Tabs.SelectMany(t=>t.Groups).SelectMany(g=>g.Buttons))
        {
            foreach(var item in new[]{b}.Concat(b.Children??new List<RibbonButtonConfig>()))if(!string.IsNullOrEmpty(item.Command)&&_currentRegistry.Resolve(item.Command)is null)throw new Exception("工具栏未接入："+item.Command);
        }
        log.Add("PASS 工具栏命令全部接入");
        if(_templateCanvas is null||!File.Exists(_templatePath))throw new Exception("启动默认模板未加载");log.Add("PASS 启动默认模板已加载");
        if(this.FindControl<TextBlock>("CurrentTemplateText") is null)throw new Exception("模板名称未显示");
        log.Add("PASS 自由表格与图区属性面板构建");
        PropertyPanel.GenericPropertyBuilder.Build(new lcdb.DrawingFrame.TemplateTable(),()=>{});PropertyPanel.GenericPropertyBuilder.Build(new lcdb.DrawingFrame.TemplateViewRegion(),()=>{});
        var tableDlg=new Slim.TableEditorDialog(new lcdb.DrawingFrame.TemplateTable{Columns=new(){new(){Title="测试",Rows=new(){"测试"}}}});
        tableDlg.Show(this);Dispatcher.UIThread.RunJobs();
        var add=tableDlg.GetVisualDescendants().OfType<Button>().Single(b=>(b.Content as string)=="＋ 列");add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
        var title=tableDlg.GetVisualDescendants().OfType<TextBox>().First(b=>b.Text=="新列");title.Text="新增列核验";Dispatcher.UIThread.RunJobs();
        var apply=tableDlg.GetVisualDescendants().OfType<Button>().Single(b=>(b.Content as string)=="应用表格");apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(tableDlg.Result?.Columns.Count!=2||tableDlg.Result.Columns[1].Title!="新增列核验")throw new Exception("自由表格增列/内容保存失败");log.Add("PASS 自由表格对话框增列、编辑和应用");
        var part=new lcdb.Optic.OpticalLens{Diameter=10};var importDlg=new Dialogs.ImportSelectionDialog(new[]{part},"缺少口径核验");importDlg.Show(this);Dispatcher.UIThread.RunJobs();
        if(!Slim.TemplatePipeline.IsIllustrative(part))throw new Exception("未标明示意口径");Slim.TemplatePipeline.ValidatePart(part);log.Add("PASS 缺少口径时自动选取不相交示意值");
        using(var bitmap=new RenderTargetBitmap(new PixelSize(960,620))){bitmap.Render(importDlg);bitmap.Save(Path.Combine(_validationOutput,"导入窗口.png"));}
        importDlg.FindControl<Button>("OkBtn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(importDlg.Result?.Count!=1)throw new Exception("示意口径无法生成选择结果");log.Add("PASS 示意口径可通过导入并保持显式标记");
    }
}
