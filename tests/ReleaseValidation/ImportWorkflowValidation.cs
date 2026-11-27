using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using lcdb;
using lcdb.Host;
using lcdb.Optic;
using lcdb.Optic.Import;
using OtoCAD.Avalonia;
using OtoCAD.Avalonia.Commands;
using OtoCAD.Avalonia.Dialogs;
using OtoCAD.Avalonia.Slim;

internal static class ImportWorkflowValidation
{
    private sealed record Selection(IReadOnlyList<Entity> Selected) : IImportSelection
    {
        public bool MakeLayoutTab => false;
        public bool MakePerElementTabs => true;
    }

    private sealed class ThirdElementHost(string file) : IAppHost
    {
        public string Title = "";
        public Task<string?> PickFileAsync(string title, params string[] patterns) => Task.FromResult<string?>(file);
        public Task<IImportSelection?> ShowImportSelectionAsync(IReadOnlyList<Entity> parts, string summary) =>
            Task.FromResult<IImportSelection?>(new Selection(new[] { parts[2] }));
        public void AddElementSheetTab(string title, Entity entity) => Title = title;
        public void AddLayoutTab(string title, IReadOnlyList<Entity> elements) => throw new InvalidOperationException();
        public void AddToActiveDocument(IReadOnlyList<Entity> elements) => throw new InvalidOperationException();
        public void RegisterCommand(string id, Action handler) { }
        public void SetStatus(string text) { }
    }

    public static void Run(string sourceRoot, string output, Action<bool,string> check)
    {
        const string sequence = "RDM;LEN\nDIM M\nSO 0 INF\nS 50 5 N-BK7\nS -50 4\nS 60 5 N-BK7\nS -60 4\nS 70 5 N-BK7\nS -70 4\nSI 0 0\nZOO 2\nGO";
        var path = Path.Combine(output, "selection-fixture.seq");
        File.WriteAllText(path, sequence);
        var host = new ThirdElementHost(path);
        ImportDesignCmd.RunAsync(host).GetAwaiter().GetResult();
        check(host.Title.Contains("元件3") && host.Title.Contains("S5/S6") && host.Title.Contains("Z1"), "仅选第三枚透镜的加工图标题保留源序号、面号和配置");

        var data = new CodeVSeqParser().ParseText(sequence, 2);
        var parts = new PrescriptionToLensBuilder().Build(data).Entities;
        CheckDialog(sourceRoot, check, parts);
        PartialImportValidation.Run(Path.Combine(output, "partial-import"), check);
    }

    private static void CheckDialog(string sourceRoot, Action<bool,string> check, List<Entity> parts)
    {
        var dialog = new ImportSelectionDialog(parts, "Z2 导入核验");
        try
        {
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            var preview = dialog.FindControl<CadCanvas>("PreviewCanvas")!;
            var panel = dialog.FindControl<StackPanel>("ItemsPanel")!;
            var row = panel.Children[2];
            var boxes = row.GetLogicalDescendants().OfType<TextBox>().ToArray();
            var label = (TextBlock)row.GetLogicalDescendants().OfType<CheckBox>().First().Content!;
            var confirm = row.GetLogicalDescendants().OfType<CheckBox>().Last();
            var ok = dialog.FindControl<Button>("OkBtn")!;
            void Click(string name) => dialog.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Click("SelectNoneBtn");
            check(!ok.IsEnabled && preview.GetAllEntities().Count == 0, "空选时生成按钮禁用且预览清空");
            var refreshes = new List<double>();
            preview.ScaleChanged += refreshes.Add;
            Click("SelectAllBtn");
            check(refreshes.Count == 1 && preview.GetAllEntities().Count == 3, "全选三枚镜片只刷新一次预览");
            boxes[0].Text = "8.125";
            boxes[1].Text = "7";
            check(!ok.IsEnabled && preview.GetAllEntities().Count == 2 && label.Text!.Contains("待修正"), "外径过小的行明确告错并从预览排除");
            boxes[1].Text = "9.25";
            confirm.IsChecked = true;
            var source = (OpticalLens)parts[2];
            var shown = (OpticalLens)preview.GetAllEntities()[2];
            check(ok.IsEnabled && label.Text!.Contains("8.125") && label.Text.Contains("9.25") && label.Text.Contains("已确认") &&
                shown.Diameter == 8.125 && shown.MechanicalDiameter == 9.25 && source.FrontSurface.SemiAperture == 8.125 / 2 &&
                !TemplatePipeline.IsIllustrative(source), "实际口径确认同步输入、摘要、曲面和实时预览");

            boxes[0].Text = "8.25";
            check(confirm.IsChecked == false && TemplatePipeline.IsIllustrative(source), "更改已确认口径后重新标记为示意");
            confirm.IsChecked = true;
            CheckDrawing(sourceRoot, check, source);

            dialog.Width = 520;
            Dispatcher.UIThread.RunJobs();
            check(Grid.GetRow(dialog.FindControl<Border>("SelectionPanel")!) == 1 &&
                Grid.GetColumn(dialog.FindControl<Border>("SelectionPanel")!) == 0, "窄窗按上下排列预览和元件编辑区");
            Click("OkBtn");
            check(dialog.Result?.Count == 3 && ReferenceEquals(dialog.Result[2], source), "导入返回已确认数值及原始顺序");
        }
        finally { if (dialog.IsVisible) dialog.Close(); }
    }
    private static void CheckDrawing(string sourceRoot, Action<bool,string> check, OpticalLens source)
    {
        var template = new Database();
        template.Open(Path.Combine(sourceRoot, "src", "OtoCAD.Avalonia", "Templates", "默认模板.otocad"));
        var sheet = TemplatePipeline.Build(template.GetEntities().ToList(), source, "元件3 [Z2 · S5/S6]", "fixture-Z2-03", "fixture.seq");
        var paper = sheet.OfType<OpticalLens>().First();
        check(ImportIdentity.ElementNumber(paper, -1) == 3 && ImportIdentity.Configuration(paper) == "Z2" &&
            ImportIdentity.Tag(paper,"apertureSource") == "user-confirmed" && paper.DrawingBinding?.Number == "fixture-Z2-03",
            "成图保留原始元件、实际口径来源和配置图号");
        check(TemplatePipeline.Values(source, "a", "b", "fixture.seq", "1:1")["源面号"] == "S5/S6",
            "自定义模板可绑定源面号");

    }
}
