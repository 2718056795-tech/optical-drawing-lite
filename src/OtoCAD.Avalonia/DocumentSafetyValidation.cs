using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using LitMath;
using OtoCAD.Avalonia.PropertyPanel;
using OtoCAD.Avalonia.Services;
using OtoCAD.Avalonia.Slim;

namespace OtoCAD.Avalonia;

public static class DocumentSafetyValidation
{
    public static IReadOnlyList<string> Run(string output)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_DRAWING_LITE_DATA_DIR")))
            throw new InvalidOperationException("文档回归必须使用独立数据目录。");
        Directory.CreateDirectory(output);
        var log = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("文档安全回归：" + message);
            log.Add("PASS " + message);
        }
        var window = new MainWindow();
        var canvas = window.ActiveCanvas;
        canvas.LoadGeneratedSheet(TemplatePipeline.Preset("ISO"));
        var panel = (PropertyPanelControl)typeof(MainWindow).GetField("_propertyPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var frame = canvas.GetAllEntities().OfType<DrawingFrame>().First(f => f is IPropertyZoneFrame);
        var cell = new PropertyCellHit("左表面", "", 0, -1, default);
        var cells = (Action<DrawingFrame, PropertyCellHit>)typeof(CadCanvas).GetField("PropertyCellSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;
        canvas.SaveCurrentScene(Path.Combine(output, "template.otocad"));
        string originalTitle = ((IPropertyZoneFrame)frame).GetCellText(cell);
        cells(frame, cell);
        panel.GetLogicalDescendants().OfType<TextBox>().First().Text = "回归标题";
        Dispatcher.UIThread.RunJobs();
        Check(!canvas.IsDirty && ((IPropertyZoneFrame)frame).GetCellText(cell) == originalTitle, "列标题输入保留为草稿");
        panel.GetLogicalDescendants().OfType<Button>().Single(x => x.Name == "FrameZoneTitleApply")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Check(canvas.IsDirty && ((IPropertyZoneFrame)frame).GetCellText(cell) == "回归标题", "列标题编辑设置未保存状态");
        Check(canvas.Undo() && ((IPropertyZoneFrame)frame).GetCellText(cell) == originalTitle, "列标题可撤销");
        Check(canvas.Redo() && ((IPropertyZoneFrame)frame).GetCellText(cell) == "回归标题", "列标题可重做");

        var part = new OpticalLens { Diameter = 10, Thickness = 4, R1 = 50, R2 = -50 };
        part.FrontSurface.SemiAperture = part.BackSurface.SemiAperture = 5;
        var template = TemplatePipeline.Preset("ISO");
        template.Add(new TemplateTable { Columns = new() { new() { Title = "厚度", Rows = new() { "d=【中心厚度】 ±0.02" } } } });
        canvas.LoadGeneratedSheet(TemplatePipeline.Build(template, part, "安全回归", "T1", "test"));
        var owner = canvas.GetAllEntities().OfType<OpticalLens>().Single();
        var thicknessTable = canvas.GetAllEntities().OfType<TemplateTable>().Single();
        var annotation = canvas.GetAllEntities().OfType<DimensionBase>().First(e =>
            TemplatePipeline.GetDirectParameterForAnnotation(canvas.GetAllEntities(), e)?.Key == "Thickness");
        string originalCaption = annotation.userText;
        panel.SetEntity(annotation);
        panel.GetLogicalDescendants().OfType<TextBox>().Single(x => x.Name == "DirectValueInput").Text = "4.375";
        panel.GetLogicalDescendants().OfType<Button>().Single(x => x.Name == "DirectValueApply")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        double Thickness() => owner.Thickness / owner.DrawingBinding!.ViewScale;
        Check(Math.Abs(Thickness() - 4.375) < 1e-9, "尺寸控件应用真实厚度");
        string editedCaption = annotation.userText;
        Check(editedCaption != originalCaption && thicknessTable.Columns[0].Rows[0] == "d=4.375 ±0.02",
            "尺寸编辑同时同步标注、参数表和手工公差");
        Check(canvas.Undo() && Math.Abs(Thickness() - 4) < 1e-9, "真实尺寸撤销恢复同一个镜片对象");
        Check(annotation.userText == originalCaption && thicknessTable.Columns[0].Rows[0] == "d=4 ±0.02",
            "撤销同时恢复标注和参数表");
        Check(canvas.Redo() && Math.Abs(Thickness() - 4.375) < 1e-9, "真实尺寸可重做");
        Check(annotation.userText == editedCaption && thicknessTable.Columns[0].Rows[0] == "d=4.375 ±0.02",
            "重做同时恢复标注和参数表");

        var text = new Text("旧要求", new Vector3(0, 0, 0), 2);
        ((Commands.ICadCommandHost)canvas).AddEntity(text);
        canvas.CommitMove(new[] { text }, new Vector2(3, 4));
        canvas.ApplyEdit(() => text.Value = "新要求");
        Check(canvas.Undo() && text.Value == "旧要求", "文字属性可撤销");
        Check(canvas.Undo() && text.Position.X == 0 && text.Position.Y == 0, "属性撤销保留此前移动操作的对象引用");
        Check(canvas.Redo() && text.Position.X == 3 && text.Position.Y == 4, "此前移动可重做");
        Check(canvas.Redo() && text.Value == "新要求", "移动后文字编辑可重做");

        var saved = Path.Combine(output, "document-safety.otocad");
        canvas.SaveCurrentScene(saved);
        canvas.LoadOtocadFile(saved);
        owner = canvas.GetAllEntities().OfType<OpticalLens>().Single();
        Check(Math.Abs(Thickness() - 4.375) < 1e-9 && !canvas.IsDirty, "保存重开保留真实尺寸并清除未保存状态");
        var badPath = Path.Combine(output, "bad.otocad");
        File.WriteAllText(badPath, "broken");
        try { canvas.LoadOtocadFile(badPath); } catch { }
        Check(canvas.GetAllEntities().Contains(owner), "打开损坏文件不会清除当前图纸");

        var create = typeof(MainWindow).GetMethod("CreateDocumentTab", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var other = (CadCanvas)create.Invoke(window, new object[] { "另一张未保存图纸", true })!;
        other.NewScene();
        ((Commands.ICadCommandHost)other).AddEntity(new Line(new Vector2(0, 0), new Vector2(2, 2)));
        other.SaveCurrentScene(Path.Combine(output, "second-document.otocad"));
        other.MarkDirty();
        canvas.MarkDirty();
        var services = (Dictionary<CadCanvas, AutoSaveService>)typeof(MainWindow)
            .GetField("_documentAutoSaves", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var firstBackup = services[canvas];
        var secondBackup = services[other];
        Check(firstBackup.BackupNow() && secondBackup.BackupNow() && firstBackup.BackupPath != secondBackup.BackupPath,
            "不同图纸独立备份，后台图纸也能备份");
        Check(File.Exists(firstBackup.BackupPath) && File.Exists(secondBackup.BackupPath), "多张未保存图纸备份同时存在");
        Check(!AutoSaveService.FindBackups().Contains(firstBackup.BackupPath), "恢复列表排除仍在运行的实例");
        var abandoned = Path.Combine(AutoSaveService.DefaultDir, "validation-abandoned");
        Directory.CreateDirectory(abandoned);
        var abandonedPath = Path.Combine(abandoned, "one.autosave.json");
        File.Copy(firstBackup.BackupPath, abandonedPath, true);
        Check(AutoSaveService.FindBackups().Contains(abandonedPath), "恢复列表发现已退出实例的图纸");
        AutoSaveService.DeleteBackup(abandonedPath);

        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.UnsavedPromptOverride = _ => Task.FromResult<string?>("取消");
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Check(!closed && File.Exists(firstBackup.BackupPath) && File.Exists(secondBackup.BackupPath),
            "取消退出保留窗口和所有恢复备份");
        var otherWindow = new MainWindow();
        otherWindow.ActiveCanvas.MarkDirty();
        var otherServices = (Dictionary<CadCanvas, AutoSaveService>)typeof(MainWindow)
            .GetField("_documentAutoSaves", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(otherWindow)!;
        var otherWindowBackup = otherServices[otherWindow.ActiveCanvas];
        Check(otherWindowBackup.BackupNow(), "另一窗口可以同时保留独立备份");
        window.UnsavedPromptOverride = _ => Task.FromResult<string?>("全部保存");
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Check(closed && !File.Exists(firstBackup.BackupPath) && !File.Exists(secondBackup.BackupPath),
            "全部保存成功后关闭并清理本窗口的备份");
        Check(!canvas.IsDirty && !other.IsDirty && File.Exists(otherWindowBackup.BackupPath),
            "退出保存全部图纸并保留另一窗口的恢复文件");
        bool otherClosed = false;
        otherWindow.Closed += (_, _) => otherClosed = true;
        otherWindow.UnsavedPromptOverride = _ => Task.FromResult<string?>("不保存");
        otherWindow.Close();
        Dispatcher.UIThread.RunJobs();
        Check(otherClosed && !File.Exists(otherWindowBackup.BackupPath), "明确放弃后清理对应窗口的备份");

        string atomic = Path.Combine(output, "atomic-save.txt");
        File.WriteAllText(atomic, "原文件");
        try { lcdb.IO.AtomicFile.Write(atomic, stream => { stream.WriteByte(1); throw new IOException("故障注入"); }); }
        catch (IOException) { }
        Check(File.ReadAllText(atomic) == "原文件", "写入失败保留旧文件");
        lcdb.IO.AtomicFile.Write(atomic, stream => { using var writer = new StreamWriter(stream, leaveOpen: true); writer.Write("新文件"); });
        Check(File.ReadAllText(atomic) == "新文件" && File.ReadAllText(atomic + ".bak") == "原文件",
            "完整写入后替换并保留上一版");
        return log;
    }
}
