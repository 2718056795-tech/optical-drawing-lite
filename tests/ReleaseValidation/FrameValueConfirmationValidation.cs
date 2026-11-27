using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using LitMath;
using OtoCAD.Avalonia;
using OtoCAD.Avalonia.PropertyPanel;
using OtoCAD.Avalonia.Slim;

internal static class FrameValueConfirmationValidation
{
    private const string NewTitle = "透镜装配图 光机-甲01";
    private const string NewNotes = "1. 双面镀增透膜\n2. 外缘涂墨";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run(MainWindow window, string output, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        var tabs = window.SidebarDocumentTabs.Where(tab =>
            tab.Content is CadCanvas c && TemplatePipeline.IsGeneratedDrawing(c.GetAllEntities())).TakeLast(2).ToArray();
        var tab = tabs.Single(t => ((CadCanvas)t.Content!).GetAllEntities().OfType<OpticalLens>().Any());
        var other = tabs.Single(t => t != tab);
        window.SelectSidebarDocument(tab);
        Layout(window);
        var canvas = window.ActiveCanvas;
        canvas.ResetView();
        Layout(window);
        var panel = window.FindControl<PropertyPanelControl>("MainPropertyPanel")!;
        VerifyTitle(window, canvas, panel, output, check);
        VerifyNotesAndSwitching(window, tab, other, panel, check);
        VerifyPersistence(canvas, output, check);
        VerifyReusableTemplate(canvas, output, check);
    }

    internal static IsoLensDrawingFrame Frame(CadCanvas c) =>
        c.GetAllEntities().OfType<IsoLensDrawingFrame>().Single();

    internal static T Control<T>(PropertyPanelControl panel, string name) where T : Control =>
        panel.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    internal static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    internal static void KeyPress(TextBox box, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });

    internal static void Layout(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        var size = window.ClientSize;
        window.Measure(size);
        window.Arrange(new Rect(size));
        Dispatcher.UIThread.RunJobs();
    }

    private static PropertyCellHit Cell(IsoLensDrawingFrame frame, string field)
    {
        // Hit-test the same rectangles that were emitted when the title block was drawn.
        frame.HitTestCell(new Vector2(frame.Origin.X, frame.Origin.Y));
        var hits = (List<PropertyCellHit>)typeof(DrawingFrame).GetField("_cellHits", Private)!.GetValue(frame)!;
        return hits.First(h => h.FieldKey == field);
    }

    internal static void Pick(MainWindow window, CadCanvas canvas, string field)
    {
        PickCell(window, canvas, Cell(Frame(canvas), field));
    }

    internal static void PickCell(MainWindow window, CadCanvas canvas, PropertyCellHit cell)
    {
        PickPoint(window, canvas, cell.Rect.location + new Vector2(cell.Rect.width, cell.Rect.height) * 0.5);
    }

    internal static void PickPoint(MainWindow window, CadCanvas canvas, Vector2 model)
    {
        double Offset(string name) => (float)typeof(CadCanvas).GetField(name, Private)!.GetValue(canvas)!;
        var local = new Avalonia.Point(Offset("_offsetX") + model.X * canvas.CurrentScale,
            Offset("_offsetY") - model.Y * canvas.CurrentScale);
        var point = canvas.TranslatePoint(local, window) ?? throw new InvalidOperationException("图纸尚未显示");
        var pointer = new Avalonia.Input.Pointer(1, PointerType.Mouse, true);
        var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        canvas.RaiseEvent(new PointerPressedEventArgs(canvas, pointer, window, point, 0, properties, KeyModifiers.None));
        canvas.RaiseEvent(new PointerReleasedEventArgs(canvas, pointer, window, point, 1,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Layout(window);
    }

    internal static Entity ScreenEntity(CadCanvas canvas, Entity source) =>
        (Entity)typeof(CadCanvas).GetMethod("GetScreenEntity", Private)!.Invoke(canvas, new[] { source })!;

    internal static bool Rendered(IsoLensDrawingFrame frame, string title)
    {
        frame.HitTestCell(frame.Origin);
        var entities = (List<Entity>)typeof(DrawingFrame).GetField("_markEntities", Private)!.GetValue(frame)!;
        return entities.OfType<MText>().Any(t => t.Value == title);
    }

    internal static void Screenshot(MainWindow window, string path)
    {
        using var shot = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height));
        shot.Render(window);
        shot.Save(path);
    }

    private static void VerifyTitle(MainWindow window, CadCanvas canvas, PropertyPanelControl panel,
        string output, Action<bool, string> check)
    {
        string original = Frame(canvas).ProjectPart;
        Pick(window, canvas, "ProjectPart");
        var input = Control<TextBox>(panel, "FrameCellValueInput");
        var apply = Control<Button>(panel, "FrameCellValueApply");
        check(window.ActiveSidebarPage == "属性" && input.Text == original && Equals(apply.Content, "确定"),
            "在默认模板单片加工图上点击项目/图号，侧栏显示当前成图值与确定按钮");
        input.Text = NewTitle;
        apply.Focus();
        Layout(window);
        check(Frame(canvas).ProjectPart == original && Rendered(Frame(canvas), original),
            "项目名称输入及失焦不提交，图纸数据仍保留原图号");
        Click(apply);
        Layout(window);
        check(Frame(canvas).ProjectPart == NewTitle && Rendered(Frame(canvas), NewTitle) && !Rendered(Frame(canvas), original),
            "点击确定后，图框的实际绘图文字立即换成中文项目/图号");
        Screenshot(window, Path.Combine(output, "项目图号确认后.png"));
        check(canvas.Undo() && Frame(canvas).ProjectPart == original, "一次撤销恢复完整的原项目/图号");
        check(canvas.Redo() && Frame(canvas).ProjectPart == NewTitle, "一次重做恢复完整的已确认项目/图号");
        Pick(window, canvas, "ProjectPart");
        var empty = Control<TextBox>(panel, "FrameCellValueInput");
        empty.Text = "";
        KeyPress(empty, Key.Enter);
        Layout(window);
        check(Frame(canvas).ProjectPart == "" && !Rendered(Frame(canvas), NewTitle), "清空并按 Enter 后图框删除旧字");
        TemplatePipeline.SynchronizeGeneratedSheet(canvas.GetAllEntities());
        check(Frame(canvas).ProjectPart == "" && canvas.Undo() && Frame(canvas).ProjectPart == NewTitle,
            "空覆盖经同步仍为空，撤销可回到上一手动值");
    }

    private static void VerifyNotesAndSwitching(MainWindow window, TabItem tab, TabItem other,
        PropertyPanelControl panel, Action<bool, string> check)
    {
        var canvas = (CadCanvas)tab.Content!;
        var second = (CadCanvas)other.Content!;
        var secondTitle = Frame(second).ProjectPart;
        Pick(window, canvas, "ProjectPart");
        Control<TextBox>(panel, "FrameCellValueInput").Text = "未确认-换字段";
        Pick(window, canvas, "NoteLine_0");
        var input = Control<TextBox>(panel, "FrameCellValueInput");
        string priorNotes = Frame(canvas).Notes;
        input.Text = NewNotes;
        KeyPress(input, Key.Enter);
        check(input.AcceptsReturn && Frame(canvas).Notes == priorNotes && Frame(canvas).ProjectPart == NewTitle,
            "切换字段丢弃项目草稿，技术要求可换行且单按 Enter 不提交");
        input.Text = NewNotes;
        KeyPress(input, Key.Enter, KeyModifiers.Control);
        Layout(window);
        check(Frame(canvas).Notes == NewNotes && NewNotes.Split('\n').All(n => Rendered(Frame(canvas), n)),
            "Ctrl+Enter 一次应用技术要求各行，图面逐行更新");
        Pick(window, canvas, "ProjectPart");
        Control<TextBox>(panel, "FrameCellValueInput").Text = "未确认-换图";
        window.SelectSidebarDocument(other);
        Layout(window);
        Pick(window, second, "ProjectPart");
        check(Control<TextBox>(panel, "FrameCellValueInput").Text == secondTitle &&
            Frame(second).ProjectPart == secondTitle && Frame(canvas).ProjectPart == NewTitle,
            "切换到另一张图纸丢弃草稿，两张图号独立");
        window.SelectSidebarDocument(tab);
        Layout(window);
    }

    private static void VerifyPersistence(CadCanvas canvas, string output, Action<bool, string> check)
    {
        var entities = canvas.GetAllEntities();
        var source = (OpticalLens)TemplatePipeline.SourcePartFromView(entities.OfType<OpticalLens>().Single());
        check(TemplatePipeline.TrySetDirectParameter(entities, "Thickness", source.Thickness + 1, out _) &&
            Frame(canvas).ProjectPart == NewTitle && Frame(canvas).Notes == NewNotes,
            "编辑镜片厚度并重新计算参数表后，手动图号及技术要求仍正确");
        var current = Path.Combine(output, "保存.otocad");
        canvas.SaveCurrentScene(current);
        var stem = Path.Combine(output, "导出");
        TemplatePipeline.Export(canvas, stem, ["otocad","json","png","pdf","dxf"]);
        foreach (var path in new[] { current, stem + ".otocad", stem + ".json" })
        {
            var reopened = new CadCanvas();
            reopened.LoadOtocadFile(path);
            var frame = Frame(reopened);
            TemplatePipeline.SynchronizeGeneratedSheet(reopened.GetAllEntities());
            check(frame.ProjectPart == NewTitle && frame.Notes == NewNotes && Rendered(frame, NewTitle),
                Path.GetFileName(path) + " 通过生产入口重开和再次同步，保留已确认文字");
        }
        check(new[] { ".png", ".pdf", ".dxf" }.All(ext => new FileInfo(stem + ext).Length > 0),
            "带手动项目/图号及多行技术要求的加工图成功导出 PNG、PDF 与 DXF");
    }

    private static void VerifyReusableTemplate(CadCanvas canvas, string output, Action<bool, string> check)
    {
        var restored = TemplatePipeline.RestoreReusableTemplate(canvas.GetAllEntities());
        var token = restored.OfType<IsoLensDrawingFrame>().Single().ProjectPart;
        var copy = new CadCanvas();
        copy.LoadGeneratedSheet(restored);
        var file = Path.Combine(output, "复用模板.otocad");
        copy.SaveCurrentScene(file);
        var library = new Database();
        library.Open(file);
        var part = (OpticalLens)TemplatePipeline.SourcePartFromView(canvas.GetAllEntities().OfType<OpticalLens>().Single());
        var next = TemplatePipeline.Build(library.GetEntities().ToList(), part, "新镜片二", "NEXT-02", "new.zmx");
        check(token.Contains('【') && next.OfType<IsoLensDrawingFrame>().Single().ProjectPart.Contains("新镜片二"),
            "另存为模板后保留原图号绑定，下一枚镜片自动填写自己的名称");
    }
}
