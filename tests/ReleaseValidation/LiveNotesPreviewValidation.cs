using Avalonia.Controls;
using Avalonia.Input;
using lcdb;
using lcdb.DrawingFrame;
using OtoCAD.Avalonia;
using OtoCAD.Avalonia.PropertyPanel;
using OtoCAD.Avalonia.Slim;
using static FrameValueConfirmationValidation;

internal static class LiveNotesPreviewValidation
{
    private const string Draft = "1. 实时草稿 LIVE-NOTE-114";

    private static IsoLensDrawingFrame ScreenFrame(CadCanvas canvas) =>
        (IsoLensDrawingFrame)ScreenEntity(canvas, Frame(canvas));

    internal static void Run(MainWindow window, string output, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        var tabs = window.SidebarDocumentTabs.Where(t => t.Content is CadCanvas c &&
            TemplatePipeline.IsGeneratedDrawing(c.GetAllEntities())).ToArray();
        var sheet = (CadCanvas)tabs[0].Content!;
        var other = (CadCanvas)tabs[1].Content!;
        window.SelectSidebarDocument(tabs[0]);
        Layout(window);
        sheet.ResetView();
        Layout(window);
        sheet.SaveCurrentScene(Path.Combine(output, "基线.otocad"));
        int before = sheet.UndoCount;
        var panel = window.FindControl<PropertyPanelControl>("MainPropertyPanel")!;
        string original = Frame(sheet).Notes;
        string text = string.Join("\n", original.Split('\n').Skip(1).Prepend(Draft));

        TextBox StartDraft()
        {
            Pick(window, sheet, "NoteLine_0");
            var edit = Control<TextBox>(panel, "FrameCellValueInput");
            edit.Text = text;
            Layout(window);
            return edit;
        }

        var draftEditor = StartDraft();
        check(Rendered(ScreenFrame(sheet), Draft) && !Rendered(Frame(sheet), Draft) &&
            Frame(sheet).Notes == original && sheet.UndoCount == before && !sheet.IsDirty,
            "技术要求输入即进入屏幕图框绘制，不修改已保存图纸、撤销数及脏标记");
        Screenshot(window, Path.Combine(output, "技术要求输入实时预览.png"));
        var draftExport = Path.Combine(output, "尚未确认");
        TemplatePipeline.Export(sheet, draftExport, ["pdf", "otocad", "dxf"]);
        var opened = new CadCanvas();
        opened.LoadOtocadFile(draftExport + ".otocad");
        check(Frame(opened).Notes == original && !File.ReadAllText(draftExport + ".dxf").Contains("LIVE-NOTE-114") &&
            Rendered(ScreenFrame(sheet), Draft), "预览期间的导出仍使用已确认内容，导出后预览保留");

        KeyPress(draftEditor, Key.Escape);
        Layout(window);
        check(draftEditor.Text == original && ScreenFrame(sheet).Notes == original &&
            !Rendered(ScreenFrame(sheet), Draft) && sheet.UndoCount == before && !sheet.IsDirty,
            "Esc 还原属性输入和屏幕图框，不增加撤销");
        StartDraft();
        Pick(window, sheet, "ProjectPart");
        check(!Rendered(ScreenFrame(sheet), Draft) && !sheet.IsDirty && sheet.UndoCount == before,
            "改选另一字段清除预览且不产生编辑记录");
        StartDraft();
        window.SelectSidebarDocument(tabs[1]);
        Layout(window);
        check(!Rendered(ScreenFrame(other), Draft) && !Rendered(ScreenFrame(sheet), Draft), "切换图纸后两个画布均无遗留草稿");
        window.SelectSidebarDocument(tabs[0]);
        Layout(window);
        StartDraft();
        Click(Control<Button>(panel, "FrameCellValueApply"));
        Layout(window);
        check(Frame(sheet).Notes == text && Rendered(ScreenFrame(sheet), Draft) &&
            sheet.UndoCount == before + 1 && sheet.IsDirty, "实时预览后点击确定只记录一次，图纸正式显示新技术要求");
        string[] otherLines = Frame(other).Notes.Split('\n');
        Click(Control<Button>(panel, "FrameCellValueApplyAll"));
        Layout(window);
        check(Frame(other).Notes.Split('\n').SequenceEqual(otherLines.Skip(1).Prepend(Draft)) &&
            sheet.UndoCount == before + 1, "先确定后批量确认只传播修改过的条款，当前图没有重复撤销项");
        string appliedExport = Path.Combine(output, "已经确认");
        TemplatePipeline.Export(sheet, appliedExport, ["pdf", "otocad", "dxf"]);
        opened.LoadOtocadFile(appliedExport + ".otocad");
        check(Frame(opened).Notes == text && File.ReadAllText(appliedExport + ".dxf").Contains("LIVE-NOTE-114") &&
            new FileInfo(appliedExport + ".pdf").Length > 0, "正式确认后的技术要求可以保存重开并进入导出");
    }
}
