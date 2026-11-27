using Avalonia.Controls;
using Avalonia.LogicalTree;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using OtoCAD.Avalonia;
using OtoCAD.Avalonia.PropertyPanel;
using OtoCAD.Avalonia.Slim;
using static FrameValueConfirmationValidation;

internal static class FrameBatchEditValidation
{
    private const string BatchLine = "1. 全组镀膜要求-工序甲";
    private const string PrivateLine = "2. 仅本片检验-保留原样";
    private const string PrivateTitle = "此片有独立图号";

    public static void Run(MainWindow window, string output, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        var all = window.SidebarDocumentTabs.Select(t => (CadCanvas)t.Content!).ToList();
        var sheets = all.Where(c => TemplatePipeline.IsGeneratedDrawing(c.GetAllEntities())).ToList();
        var source = sheets.First(c => c.GetAllEntities().OfType<OpticalLens>().Any());
        var custom = sheets.Last(c => c != source && c.GetAllEntities().OfType<OpticalLens>().Any());
        var glue = sheets.Single(c => c.GetAllEntities().OfType<CementedLens>().Any());
        var panel = window.FindControl<PropertyPanelControl>("MainPropertyPanel")!;
        SetField(window, custom, panel, "ProjectPart", PrivateTitle);
        string[] ownLines = Frame(custom).Notes.Split('\n');
        ownLines[1] = PrivateLine;
        SetField(window, custom, panel, "NoteLine_0", string.Join("\n", ownLines));
        var excluded = all.Except(sheets).ToDictionary(c => c, Snapshot);
        var geometry = sheets.ToDictionary(c => c, SnapshotGeometry);
        VerifySingleLineBatch(window, sheets, source, panel, output, check);
        check(Frame(custom).ProjectPart == PrivateTitle && Frame(custom).Notes.Split('\n')[1] == PrivateLine,
            "技术要求批量保留另一张图的独立图号和专有条款");
        VerifyTitleAndDescription(window, sheets, source, panel, check);
        foreach (var sheet in sheets)
            check(geometry[sheet] == SnapshotGeometry(sheet), "批量文字编辑保留原处方曲率、厚度及材料");
        check(excluded.Keys.Any(c => c.GetAllEntities().OfType<IsoLensDrawingFrame>().Any()) &&
              excluded.Keys.Any(c => c.GetAllEntities().Any(e => e is OpticalLens or CementedLens)) &&
              excluded.All(kv => Snapshot(kv.Key) == kv.Value), "模板及光路总览不参与批量加工图编辑");
        VerifyExports(new[] { source, glue, custom }, output, check);
        VerifyNumericBatch(window, sheets, source, panel, geometry, check);
    }

    private static void Show(MainWindow window, CadCanvas sheet)
    {
        window.SelectSidebarDocument(window.SidebarDocumentTabs.Single(t => ReferenceEquals(t.Content, sheet)));
        Layout(window);
        sheet.ResetView();
        Layout(window);
    }

    private static void SetField(MainWindow window, CadCanvas sheet, PropertyPanelControl panel, string field, string text)
    {
        Show(window, sheet);
        Pick(window, sheet, field);
        Control<TextBox>(panel, "FrameCellValueInput").Text = text;
        Click(Control<Button>(panel, "FrameCellValueApply"));
        Layout(window);
    }

    private static void VerifySingleLineBatch(MainWindow window, List<CadCanvas> sheets, CadCanvas source,
        PropertyPanelControl panel, string output, Action<bool, string> check)
    {
        Show(window, source);
        var old = sheets.ToDictionary(c => c, c => Frame(c).Notes);
        string ReplaceFirst(string notes) => string.Join("\n", notes.Split('\n').Skip(1).Prepend(BatchLine));
        Pick(window, source, "NoteLine_0");
        Control<TextBox>(panel, "FrameCellValueInput").Text = ReplaceFirst(old[source]);
        Click(Control<Button>(panel, "FrameCellValueApply"));
        Layout(window);
        check(sheets.All(c => Frame(c).Notes == (c == source ? ReplaceFirst(old[c]) : old[c])),
            "先点确定只更新当前图，其余已打开图纸保持各自原文");
        var applyAll = Control<Button>(panel, "FrameCellValueApplyAll");
        check(Equals(applyAll.Content, "应用到所有图纸"), "当前加工图提供应用到所有图纸按钮");
        Click(applyAll);
        Layout(window);
        check(sheets.All(c => Frame(c).Notes == ReplaceFirst(old[c])),
            "确定后再应用到所有图纸，仅替换各张图的同一条技术要求");
        Screenshot(window, Path.Combine(output, "技术要求批量应用后.png"));
        foreach (var sheet in sheets)
        {
            check(sheet.Undo() && Frame(sheet).Notes == old[sheet], "每张加工图单独撤销后恢复自己的技术要求");
            check(sheet.Redo() && Frame(sheet).Notes == ReplaceFirst(old[sheet]), "每张加工图单独重做后恢复同条批量修改");
        }
    }

    private static void VerifyTitleAndDescription(MainWindow window, List<CadCanvas> sheets, CadCanvas source,
        PropertyPanelControl panel, Action<bool,string> check)
    {
        Show(window, source);
        Pick(window, source, "ProjectPart");
        Control<TextBox>(panel, "FrameCellValueInput").Text = "整组图号-批量";
        Click(Control<Button>(panel, "FrameCellValueApplyAll"));
        Layout(window);
        check(sheets.All(c => Frame(c).ProjectPart == "整组图号-批量"), "直接点击批量确认将同字段标题写入各张单片及胶合图");
        var before = sheets.ToDictionary(c => c, c => Frame(c).Columns[0].Rows[0]);
        var row = Frame(source).GetCell(0, 0) ?? throw new InvalidOperationException("缺少默认曲率行");
        PickCell(window, source, row);
        var edit = Control<TextBox>(panel, "BoundDescriptionInput");
        check(edit.Text?.Contains('【') == true, "通过点击图纸半径行打开描述及公差的模板表达式");
        edit.Text = edit.Text + "（复核）";
        Click(Control<Button>(panel, "FrameCellValueApplyAll"));
        Layout(window);
        check(sheets.All(c => Frame(c).Columns[0].Rows[0] == before[c] + "（复核）"),
            "批量修改半径行描述后，各图仍渲染自己的原半径值");
    }

    private static void VerifyNumericBatch(MainWindow window, List<CadCanvas> sheets, CadCanvas source,
        PropertyPanelControl panel, Dictionary<CadCanvas, string> geometry, Action<bool, string> check)
    {
        Show(window, source);
        var row = Frame(source).GetCell(0, 0) ?? throw new InvalidOperationException("缺少默认曲率行");
        PickCell(window, source, row);
        var input = Control<TextBox>(panel, "DirectValueInput");
        var apply = Control<Button>(panel, "DirectValueApplyAll");
        check(Equals(input.Tag, "R1") && Equals(apply.Tag, "R1"), "前表面半径数值和批量操作对应 R1");
        input.Text = "150";
        Click(apply);
        Layout(window);
        foreach (var sheet in sheets)
        {
            var value = TemplatePipeline.GetDirectParametersForCell(sheet.GetAllEntities(), Frame(sheet), 0, 0).Single();
            check(value.Key == "R1" && Math.Abs(value.Value - 150) < 1e-9 &&
                Frame(sheet).Columns[0].Rows[0].Contains("150") && Frame(sheet).Columns[0].Rows[0].Contains("复核"),
                "批量修改前表面真实半径，单片和胶合图参数表各自更新并保留描述");
            check(sheet.Undo() && geometry[sheet] == SnapshotGeometry(sheet),
                "各张图可分别撤销前表面半径的批量数值编辑");
        }
    }

    private static string Snapshot(CadCanvas c) =>
        string.Join("|", c.GetAllEntities().OfType<IsoLensDrawingFrame>().Select(f => f.ProjectPart + f.Notes)) +
        SnapshotGeometry(c) + "|" + c.GetAllEntities().Count;

    private static string SnapshotGeometry(CadCanvas c) => string.Join("|", c.GetAllEntities()
        .Where(e => e is OpticalLens or CementedLens)
        .Select(e => e switch
        {
            OpticalLens l => $"{l.R1:R},{l.R2:R},{l.Thickness:R},{l.MaterialName}",
            CementedLens g => $"{g.R1:R},{g.RContact:R},{g.R3:R},{g.T1:R},{g.T2:R},{g.Material1},{g.Material2}",
            _ => throw new InvalidOperationException()
        }));

    private static void VerifyExports(CadCanvas[] sheets, string output, Action<bool,string> check)
    {
        for (int i=0; i<sheets.Length; i++)
        {
            var c = sheets[i];
            var stem = Path.Combine(output, "batch-" + i);
            TemplatePipeline.Export(c, stem, ["otocad","pdf"]);
            var reopened = new CadCanvas();
            reopened.LoadOtocadFile(stem + ".otocad");
            var expected = Frame(c);
            var saved = Frame(reopened);
            check(saved.ProjectPart == expected.ProjectPart && saved.Notes == expected.Notes &&
                saved.Columns[0].Rows[0] == expected.Columns[0].Rows[0] && SnapshotGeometry(c) == SnapshotGeometry(reopened) &&
                new FileInfo(stem + ".pdf").Length > 0, "批量后的独立图纸导出读回保留自己的半径、材料、条款及标题");
        }
    }
}
