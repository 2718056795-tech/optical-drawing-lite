using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using lcdb;
using lcdb.Optic;
using lcdb.Rendering;
using LitMath;
using OtoCAD.Avalonia;
using OtoCAD.Avalonia.PropertyPanel;
using OtoCAD.Avalonia.Slim;
using static FrameValueConfirmationValidation;

internal static class DimensionTextHeightValidation
{
    internal static void Run(MainWindow window, string output, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        var tab = window.SidebarDocumentTabs.Last(t => t.Content is CadCanvas c &&
            TemplatePipeline.IsGeneratedDrawing(c.GetAllEntities()) && c.GetAllEntities().OfType<OpticalLens>().Any());
        window.SelectSidebarDocument(tab);
        Layout(window);
        var sheet = (CadCanvas)tab.Content!;
        sheet.ResetView();
        Layout(window);
        var panel = window.FindControl<PropertyPanelControl>("MainPropertyPanel")!;
        string diameter = sheet.GetAllEntities().OfType<LinearDimension>().Select(d =>
            TemplatePipeline.GetDirectParameterForAnnotation(sheet.GetAllEntities(), d)?.Key)
            .First(k => k is "MechanicalDiameter" or "Diameter")!;
        string[] keys = { "Thickness", diameter };
        var before = keys.ToDictionary(k => k, k => Glyph(Annotation(sheet, k)).Height);
        var captions = keys.ToDictionary(k => k, k => Glyph(Annotation(sheet, k)).Value);
        var baseline = Path.Combine(output, "调整前");
        TemplatePipeline.Export(sheet, baseline, ["pdf", "dxf"]);
        var initialUndo = sheet.UndoCount;
        Select(window, sheet, Annotation(sheet, "Thickness"));
        var presets = Control<ComboBox>(panel, "DimensionTextHeightPreset");
        var input = Control<TextBox>(panel, "DimensionTextHeightInput");
        double chosen = SetPreset(presets, input, before["Thickness"]);
        Layout(window);
        var thickness = Annotation(sheet, "Thickness");
        check(Equal(Glyph((LinearDimension)ScreenEntity(sheet, thickness)).Height, chosen) &&
            Equal(Glyph(thickness).Height, before["Thickness"]), "选择字高预设立即预览，正式尺寸文字等待应用");
        Click(Control<Button>(panel, "DimensionTextHeightApply"));
        Layout(window);
        check(Equal(Glyph(Annotation(sheet, "Thickness")).Height, chosen) &&
            Equal(Glyph(Annotation(sheet, diameter)).Height, before[diameter]) && sheet.UndoCount == initialUndo + 1,
            "画布选中中心厚度标注应用预设字高，只改变所选标注并可一步撤销");
        Select(window, sheet, Annotation(sheet, diameter));
        const double custom = 5.75;
        var diameterInput = Control<TextBox>(panel, "DimensionTextHeightInput");
        var outer = Annotation(sheet, diameter);
        diameterInput.Text = "8.75";
        Layout(window);
        check(Equal(Glyph((LinearDimension)ScreenEntity(sheet, outer)).Height, 8.75), "尺寸字号手输后实时预览");
        KeyPress(diameterInput, Key.Escape);
        Layout(window);
        check(Equal(Glyph((LinearDimension)ScreenEntity(sheet, outer)).Height, before[diameter]) &&
            sheet.UndoCount == initialUndo + 1, "取消未确认的尺寸字号恢复原字高，不产生撤销项");
        diameterInput.Text = custom.ToString(CultureInfo.InvariantCulture);
        Layout(window);
        Click(Control<Button>(panel, "DimensionTextHeightApply"));
        Layout(window);
        check(Equal(Glyph(Annotation(sheet, diameter)).Height, custom) && sheet.UndoCount == initialUndo + 2 &&
            captions.All(kv => Glyph(Annotation(sheet, kv.Key)).Value == kv.Value),
            "在实际外径标注属性输入 5.75 mm 字高，厚度与外径的尺寸真值保持不变");
        Screenshot(window, Path.Combine(output, "厚度与直径字高已应用.png"));
        string native = Path.Combine(output, "标注字号保存.otocad");
        sheet.SaveCurrentScene(native);
        var changed = Path.Combine(output, "调整后");
        TemplatePipeline.Export(sheet, changed, ["json", "pdf", "png", "dxf"]);
        foreach (string path in new[] { native, changed + ".json" })
        {
            var reopened = new CadCanvas();
            reopened.LoadOtocadFile(path);
            TemplatePipeline.SynchronizeGeneratedSheet(reopened.GetAllEntities());
            check(Equal(Glyph(Annotation(reopened, "Thickness")).Height, chosen) &&
                Equal(Glyph(Annotation(reopened, diameter)).Height, custom),
                Path.GetExtension(path) + " 重开并同步后，厚度和外径按各自指定字高绘制");
        }
        var oldDxf = netDxf.DxfDocument.Load(baseline + ".dxf");
        var newDxf = netDxf.DxfDocument.Load(changed + ".dxf");
        foreach (string key in keys)
        {
            double expected = key == "Thickness" ? chosen : custom;
            check(Equal(oldDxf.Entities.Texts.Single(t => t.Value == captions[key]).Height, before[key]) &&
                Equal(newDxf.Entities.Texts.Single(t => t.Value == captions[key]).Height, expected),
                "DXF 读回 " + key + " 原始与新字高分别匹配实际尺寸文字");
        }
        check(sheet.Undo() && Equal(Glyph(Annotation(sheet, diameter)).Height, before[diameter]) &&
            Equal(Glyph(Annotation(sheet, "Thickness")).Height, chosen), "撤销自定义外径字高不会影响厚度的预设字高");
        check(sheet.Undo() && Equal(Glyph(Annotation(sheet, "Thickness")).Height, before["Thickness"]) &&
            sheet.Redo() && sheet.Redo() && Equal(Glyph(Annotation(sheet, diameter)).Height, custom), "两个字号操作可依次撤销及重做");
        VerifyBatchThickness(window, sheet, panel, check);
    }

    private static void VerifyBatchThickness(MainWindow window, CadCanvas source, PropertyPanelControl panel, Action<bool, string> check)
    {
        const double batch = 4.25;
        var sheets = window.SidebarDocumentTabs.Select(t => (CadCanvas)t.Content!)
            .Where(c => TemplatePipeline.IsGeneratedDrawing(c.GetAllEntities())).ToArray();
        var originals = (from c in sheets
                         let entities = c.GetAllEntities()
                         from dimension in entities.OfType<LinearDimension>()
                         select (Sheet: c, Dimension: dimension, Old: Glyph(dimension).Height,
                             Match: TemplatePipeline.GetDirectParameterForAnnotation(entities, dimension)?.Key == "Thickness")).ToArray();
        Select(window, source, Annotation(source, "Thickness"));
        Control<TextBox>(panel, "DimensionTextHeightInput").Text = "4.25";
        Layout(window);
        Click(Control<Button>(panel, "DimensionTextHeightApplyAll"));
        Layout(window);
        check(originals.Count(item => item.Match) > 1 && originals.Any(item =>
            item.Sheet.GetAllEntities().OfType<CementedLens>().Any()) &&
            originals.All(item => Equal(Glyph(item.Dimension).Height, item.Match ? batch : item.Old)),
            "跨图批量只改变单片中心厚度字高，双胶合片各厚度、外径及其他标注保留原字号");
        Click(Control<Button>(panel, "DimensionTextHeightReset"));
        Layout(window);
        var selected = Annotation(source, "Thickness");
        check(selected.TextHeightOverride is null && Equal(Glyph(selected).Height, selected.style.TextHeight),
            "恢复默认后当前标注重新继承样式字高");
    }

    private static double SetPreset(ComboBox presets, TextBox input, double original)
    {
        foreach (var item in presets.Items)
        {
            presets.SelectedItem = item;
            if (double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                value >= 3.5 && !Equal(value, original)) return value;
        }
        throw new InvalidOperationException("没有可选的数字标注字号");
    }

    private static bool Equal(double left, double right) => Math.Abs(left - right) < 1e-6;

    private static LinearDimension Annotation(CadCanvas sheet, string key)
    {
        var entities = sheet.GetAllEntities();
        return entities.OfType<LinearDimension>().Single(d =>
            TemplatePipeline.GetDirectParameterForAnnotation(entities, d)?.Key == key);
    }

    private static DxfGraphicsDraw Draw(LinearDimension dimension)
    {
        var graphics = new DxfGraphicsDraw();
        dimension.Draw(graphics);
        return graphics;
    }

    private static netDxf.Entities.Text Glyph(LinearDimension dimension)
    {
        var runs = Draw(dimension).Entities.OfType<netDxf.Entities.Text>().ToArray();
        // Mixed-language callouts may be split into separate styles with the same paper height.
        return runs.Length > 0 && runs.All(run => Equal(run.Height, runs[0].Height))
            ? runs[0]
            : throw new InvalidOperationException("标注各文字片段未使用统一字高");
    }

    private static void Select(MainWindow window, CadCanvas sheet, LinearDimension dimension)
    {
        // Sample the visible dimension line and its extension lines, just as a pointer would.
        sheet.ClearSelection();
        var lines = Draw(dimension).Entities.OfType<netDxf.Entities.Line>();
        foreach (var line in lines)
        {
            foreach (double t in new[] { .5, .25, .75, .1, .9 })
            {
                var a = line.StartPoint;
                var b = line.EndPoint;
                var p = new Vector2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                if (!ReferenceEquals(sheet.HitTest(p, 5 / sheet.CurrentScale), dimension)) continue;
                PickPoint(window, sheet, p);
                if (sheet.SelectedEntities.Contains(dimension)) return;
            }
        }
        throw new InvalidOperationException("真实画布上未能选中尺寸 " + dimension.userText);
    }
}
