using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using OtoCAD.Avalonia;
using OtoCAD.Avalonia.PropertyPanel;
using OtoCAD.Avalonia.Slim;
using static FrameValueConfirmationValidation;

internal static class ParamTableStyleValidation
{
    private static IsoLensDrawingFrame Screen(CadCanvas canvas, IsoLensDrawingFrame frame) =>
        (IsoLensDrawingFrame)typeof(CadCanvas).GetMethod("GetScreenEntity", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(canvas, [frame])!;

    private static double RowPitch(IsoLensDrawingFrame frame)
    {
        var first = frame.GetCell(0, 0)!.Value;
        var second = frame.GetCell(0, 1)!.Value;
        return first.Rect.location.Y - second.Rect.location.Y;
    }

    private static double TextHeight(IsoLensDrawingFrame frame, string text)
    {
        frame.GetCell(0, 0);
        var field = typeof(DrawingFrame).GetField("_markEntities", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((List<Entity>)field.GetValue(frame)!).OfType<MText>().First(t => t.Value == text).Height;
    }

    public static void Run(MainWindow window, string output, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        var sheets = window.SidebarDocumentTabs.Select(t => (CadCanvas)t.Content!)
            .Where(c => TemplatePipeline.IsGeneratedDrawing(c.GetAllEntities())).ToList();
        var source = sheets.First(c => c.GetAllEntities().OfType<OpticalLens>().Any());
        var glue = sheets.Single(c => c.GetAllEntities().OfType<CementedLens>().Any());
        window.SelectSidebarDocument(window.SidebarDocumentTabs.Single(t => t.Content == source));
        Layout(window);
        source.ResetView();
        Layout(window);
        var frame = Frame(source);
        var panel = window.FindControl<PropertyPanelControl>("MainPropertyPanel")!;
        var old = sheets.ToDictionary(c => c, c => RowPitch(Frame(c)));
        var row = frame.Columns[0].Rows[0];
        var notes = frame.Notes;
        PickCell(window, source, frame.GetCell(0, 0)!.Value);
        TextBox Input(string name) => Control<TextBox>(panel, name);
        void Apply() => Click(Control<Button>(panel, "TableFormatApply"));
        Input("TableRowPitch").Text = "4.5";
        Layout(window);
        check(RowPitch(Screen(source, frame)) == 4.5 && RowPitch(frame) == old[source],
            "参数表输入行间距，屏幕命中区先收紧，已确认图纸仍保留旧行距");
        KeyPress(Input("TableRowPitch"), Key.Escape);
        check(RowPitch(Screen(source, frame)) == old[source], "Esc 恢复原参数表行距");
        Input("TableRowPitch").Text = "0";
        check(!Control<Button>(panel, "TableFormatApply").IsEnabled, "无效行间距保留草稿并禁止确认");
        Input("TableRowPitch").Text = "4.5";
        Input("TableBodyHeight").Text = "1.85";
        Input("TableHeaderHeight").Text = "2.15";
        Apply();
        check(RowPitch(frame) == 4.5 && TextHeight(frame, row) == 1.85 && TextHeight(frame, frame.Columns[0].Title) == 2.15 &&
            frame.Notes == notes && Frame(glue).SpecificationBodyHeight is null, "当前格式仅更改本张表格的正文、表头和行间距");
        source.SaveCurrentScene(Path.Combine(output, "当前格式.otocad"));
        var loaded = new CadCanvas();
        loaded.LoadOtocadFile(Path.Combine(output, "当前格式.otocad"));
        check(RowPitch(Frame(loaded)) == 4.5 && TextHeight(Frame(loaded), row) == 1.85,
            "参数表行距和图面字体大小保存重开后保留");
        Input("TableRowPitch").Text = "4.25";
        Apply();
        Click(Control<Button>(panel, "TableFormatApplyAll"));
        Layout(window);
        check(sheets.All(c => RowPitch(Frame(c)) == 4.25) && Frame(glue).SpecificationBodyHeight is null,
            "先单图确认再批量时，只同步本次改的行间距，胶合表独立字号保留");
        Input("TableBodyHeight").Text = "2.05";
        Click(Control<Button>(panel, "TableFormatApplyAll"));
        Layout(window);
        check(sheets.All(c => TextHeight(Frame(c), Frame(c).Columns[0].Rows[0]) == 2.05) &&
            Frame(glue).SpecificationHeaderHeight is null, "选中属性行可批量修改正文字高，保留其他图纸表头");
        Screenshot(window, Path.Combine(output, "参数表格式.png"));
        TemplatePipeline.Export(source, Path.Combine(output,"参数表格式"), ["pdf","dxf"]);
        foreach (var sheet in sheets)
            check(sheet.Undo() && TextHeight(Frame(sheet),Frame(sheet).Columns[0].Rows[0]) != 2.05,
                "单片及胶合图纸可分别撤销表格格式变更");
    }
}
