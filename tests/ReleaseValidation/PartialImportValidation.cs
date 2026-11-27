using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using lcdb;
using lcdb.Optic;
using lcdb.Optic.Import;
using OtoCAD.Avalonia;
using OtoCAD.Avalonia.Commands;
using OtoCAD.Avalonia.Dialogs;
using OtoCAD.Avalonia.Services;
using OtoCAD.Avalonia.Slim;

internal static class PartialImportValidation
{
    public static void Run(string output, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            VerifyMixed(window, output, check);
            VerifyAllUnsupported(window, output, check);
            ChipZoneDrawingValidation.Run(window, Path.Combine(output, "chip-zone"), check);
            FrameValueConfirmationValidation.Run(window, Path.Combine(output, "frame-edit"), check);
            FrameBatchEditValidation.Run(window, Path.Combine(output, "frame-batch"), check);
            LiveNotesPreviewValidation.Run(window, Path.Combine(output, "notes-preview"), check);
            DimensionTextHeightValidation.Run(window, Path.Combine(output, "dimension-height"), check);
            ParamTableStyleValidation.Run(window, Path.Combine(output, "parameter-table-style"), check);
        }
        finally
        {
            foreach (var modal in window.OwnedWindows.ToArray()) modal.Close();
            int number = 0;
            foreach (var canvas in window.SidebarDocumentTabs.Select(tab => (CadCanvas)tab.Content!).Where(c => c.IsDirty))
                canvas.SaveCurrentScene(Path.Combine(output, $"窗口图纸-{++number}.otocad"));
            window.Close();
        }
    }

    private static Task Import(MainWindow window, string output, string filename, bool allUnsupported)
    {
        string path = Path.Combine(output, filename);
        File.WriteAllText(path, Prescription(allUnsupported));
        return ImportFile(window, path);
    }

    internal static Task ImportFile(MainWindow window, string path)
    {
        // Use the ordinary command after the native file picker returns a path.
        var command = typeof(ImportDesignCmd).GetMethod("RunFileAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var afterImport = (Action)typeof(MainWindow).GetMethod("ShowImportedDrawings", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate(typeof(Action), window);
        return (Task)command.Invoke(null, [window, path, afterImport])!;
    }

    internal static void Wait(Func<bool> done)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            Dispatcher.UIThread.RunJobs();
            if (timer.ElapsedMilliseconds > 5000) throw new TimeoutException("部分导入窗口未完成预期操作");
            System.Threading.Thread.Sleep(5);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static string ReportSince(HashSet<string> before) =>
        File.ReadAllText(Directory.GetFiles(AppDataPaths.ImportReportsDirectory).Single(path => !before.Contains(path)));

    private static HashSet<string> Reports() => Directory.GetFiles(AppDataPaths.ImportReportsDirectory).ToHashSet();

    private static void VerifyMixed(MainWindow window, string output, Action<bool, string> check)
    {
        var initialTabs = window.SidebarDocumentTabs.ToArray();
        var previous = Reports();
        var mixed = Import(window, output, "mixed.zmx", false);
        Wait(() => window.OwnedWindows.Any());
        var reviewWindow = window.OwnedWindows.Single();
        check(reviewWindow is ImportSelectionDialog, "混合设计直接进入唯一的元件选择窗口");
        var selection = (ImportSelectionDialog)reviewWindow;
        SelectSurvivingElements(selection, check);
        Wait(() => mixed.IsCompleted);
        mixed.GetAwaiter().GetResult();
        var added = window.SidebarDocumentTabs.Except(initialTabs).Select(tab => (CadCanvas)tab.Content!).ToArray();
        var sheets = added.Where(c => TemplatePipeline.IsGeneratedDrawing(c.GetAllEntities())).ToArray();
        check(window.ActiveSidebarPage == "图纸" && added.Length == 3 && sheets.Length == 2 &&
              added.Except(sheets).Single().GetAllEntities().OfType<OpticalLens>().Count() == 2,
            "正式主窗生成两张加工图和一个局部总览，未为不完整胶合件创建标签");
        VerifyExportedDrawings(sheets, output, check);
        var report = ReportSince(previous);
        check(report.Contains("S4") && report.Contains("TOROIDAL") && report.Contains("S8") && report.Contains("生成加工图 2 张"),
            "混合处方的落盘报告包含局部跳过原因与实际生成张数");
    }

    private static void SelectSurvivingElements(ImportSelectionDialog selection, Action<bool, string> check)
    {
        var header = selection.FindControl<TextBlock>("SummaryText")!.Text!;
        var detail = selection.FindControl<SelectableTextBlock>("ImportDetailsText")!.Text!;
        check(header.Contains("可选出图 2") && header.Contains("跳过 1") && detail.Contains("TOROIDAL") && detail.Contains("S4") &&
              detail.Contains("胶合") && detail.Contains("COORDBRK") && detail.Contains("S8"),
            "一个汇总列出可生成数、整组跳过的胶合面及独立坐标面");
        check(selection.FindControl<StackPanel>("ItemsPanel")!.Children.Count == 2 &&
              selection.FindControl<CadCanvas>("PreviewCanvas")!.GetAllEntities().Count == 2,
            "选择列表与光路预览均只含跳过胶合组前后的两个单片");
        var overview = selection.FindControl<CheckBox>("LayoutTabCheck")!;
        check(overview.Content!.ToString()!.Contains("部分"), "部分光路总览明确交代显示范围");
        overview.IsChecked = true;
        selection.FindControl<Button>("OkBtn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void VerifyExportedDrawings(CadCanvas[] sheets, string output, Action<bool, string> check)
    {
        var expected = new[] { (Id: 1, Faces: "S1/S2", Glass: "H-LAK53B"), (Id: 3, Faces: "S6/S7", Glass: "H-K9L") };
        for (int i = 0; i < sheets.Length; i++)
        {
            var stem = Path.Combine(output, "part-" + expected[i].Id);
            TemplatePipeline.Export(sheets[i], stem, ["otocad", "pdf"]);
            var loaded = new CadCanvas();
            loaded.LoadOtocadFile(stem + ".otocad");
            var lens = loaded.GetAllEntities().OfType<OpticalLens>().Single();
            check(ImportIdentity.ElementNumber(lens, 0) == expected[i].Id && ImportIdentity.Tag(lens, "sourceSurfaces") == expected[i].Faces &&
                  lens.DrawingBinding!.Number == "mixed-" + expected[i].Id.ToString("00") && lens.MaterialName == expected[i].Glass &&
                  Encoding.ASCII.GetString(File.ReadAllBytes(stem + ".pdf"), 0, 5) == "%PDF-",
                $"加工图读回保留元件 {expected[i].Id} 的原序号、源面号、图号和玻璃，PDF 成功输出");
        }
    }

    private static void VerifyAllUnsupported(MainWindow window, string output, Action<bool, string> check)
    {
        var previous = Reports();
        var beforeAllSkipped = window.SidebarDocumentTabs.ToArray();
        var unsupported = Import(window, output, "all-unsupported.zmx", true);
        Wait(() => window.OwnedWindows.Any());
        var notice = window.OwnedWindows.Single();
        check(notice.Title == "没有可出图的元件" && notice.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text?.Contains("可选出图 0") == true && t.Text.Contains("TOROIDAL")),
            "全部不支持只弹一次明确结果，显示源面原因");
        notice.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "确定"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Wait(() => unsupported.IsCompleted);
        unsupported.GetAwaiter().GetResult();
        check(window.SidebarDocumentTabs.SequenceEqual(beforeAllSkipped) && ReportSince(previous).Contains("生成加工图 0 张"),
            "全部不支持保留原标签和当前图纸，写入零张结果报告");
    }

    private static string Prescription(bool allUnsupported)
    {
        var text = new StringBuilder("VERS 190128 693 123456\nMODE SEQ\nUNIT MM\n");
        void Surface(int number, string type, string curvature, string distance, string? glass = null)
        {
            text.AppendLine($"SURF {number}").AppendLine(" TYPE " + type).AppendLine(" CURV " + curvature)
                .AppendLine(" DISZ " + distance).AppendLine(" DIAM 4");
            if (glass is not null) text.AppendLine(" GLAS " + glass);
        }
        Surface(0, "STANDARD", "0", "INFINITY");
        Surface(1, allUnsupported ? "TOROIDAL" : "STANDARD", "0.02", "4", "H-LAK53B");
        Surface(2, "STANDARD", "-0.01", "5");
        Surface(3, "STANDARD", "0.02", "4", "H-K9L");
        Surface(4, "TOROIDAL", "0.01", "3", "H-LAK53B");
        Surface(5, "STANDARD", "-0.01", "5");
        Surface(6, "STANDARD", "0.02", "5", "H-K9L");
        Surface(7, allUnsupported ? "TOROIDAL" : "STANDARD", "-0.01", "5");
        Surface(8, "COORDBRK", "0", "5");
        Surface(9, "STANDARD", "0", "0");
        return text.ToString();
    }
}
