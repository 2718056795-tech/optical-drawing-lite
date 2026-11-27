using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using OtoCAD.Avalonia;
using OtoCAD.Avalonia.Slim;

internal static class SerifOutputValidation
{
    private const string Chinese = "表面粗糙度";
    private const string Symbols = "Ø ± μ ∞ °";
    private const string SwitchName = "OtoCAD.Drawing.UseBundledSerifOnly";

    internal static void CheckInterface(Window window, Action<bool, string> check)
    {
        using var layout = new TextLayout("Ra0.8光学", new Typeface(window.FontFamily), 18, Brushes.Black);
        var runs = layout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().ToArray();
        check(runs.Any(run => run.Text.Span.Contains('光') && IsCjkFace(run.GlyphRun.GlyphTypeface.FamilyName)),
            "窗口汉字实际使用思源宋体");
        string westernFace = runs.First(run => run.Text.Span.Contains('R')).GlyphRun.GlyphTypeface.FamilyName;
        check(IsSerifFace(westernFace) && (EngineeringSerif.FindLatinFont() is null || westernFace.Contains("Anthropic", StringComparison.OrdinalIgnoreCase)),
            "窗口数字与英文字形来自 " + westernFace);
        check(runs.All(run => run.GlyphRun.GlyphInfos.All(info => info.GlyphIndex != 0)),
            "界面中英文和数字混排均可解析到实际字形");
    }

    internal static void Run(string library, string output, OpticalLens lens, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        var db = new Database();
        db.Open(Path.Combine(library, "默认模板.otocad"));
        var sheet = TemplatePipeline.Build(db.GetEntities().ToList(), lens, "透镜 " + lens.MaterialName, "L-001", "sample.seq");
        var frame = sheet.OfType<IsoLensDrawingFrame>().Single();
        frame.Notes = "1. " + Chinese + " Ra 0.8\n2. 光学符号 " + Symbols;
        frame.InvalidateRenderCache();
        check(frame.UseEngineeringSerif && sheet.OfType<DimensionBase>().All(d => d.UseEngineeringSerif),
            "默认模板的图框与生成尺寸采用同一字体方案");
        int CountFrameEntities()
        {
            var draw = new lcdb.Rendering.DxfGraphicsDraw();
            frame.Draw(draw);
            return draw.Entities.Count;
        }
        int count = CountFrameEntities();
        int[] rendered = new int[30];
        Parallel.Invoke(
            () => { for (int i = 0; i < rendered.Length; i++) { frame.InvalidateRenderCache(); CountFrameEntities(); } },
            () => { for (int i = 0; i < rendered.Length; i++) rendered[i] = CountFrameEntities(); });
        check(count > 0 && rendered.All(n => n == count), "绘制与属性刷新同时执行时每一帧内容齐全");
        var canvas = new CadCanvas();
        canvas.LoadGeneratedSheet(sheet);
        string stem = Path.Combine(output, "字体样张");
        TemplatePipeline.Export(canvas, stem, ["otocad", "json", "dxf", "pdf", "png"]);
        var dxfText = VerifyDxf(stem, false, check);

        var reopened = new CadCanvas();
        reopened.LoadOtocadFile(stem + ".otocad");
        var saved = reopened.GetAllEntities().OfType<IsoLensDrawingFrame>().Single();
        check(saved.UseEngineeringSerif && saved.Notes == frame.Notes, "原生图纸重新打开后保留字体方案和可编辑文本");
        string again = Path.Combine(output, "重开");
        TemplatePipeline.Export(reopened, again, ["dxf", "pdf"]);
        var againText = VerifyDxf(again, false, check);
        check(againText.SequenceEqual(dxfText), "重开前后的 DXF 字形串、字体样式与纸面字高一致");

        string legacy = Path.Combine(output, "旧图");
        var old = JsonNode.Parse(File.ReadAllText(stem + ".json"))!;
        RemoveFontProfile(old);
        File.WriteAllText(legacy + ".json", old.ToJsonString());
        var oldCanvas = new CadCanvas();
        oldCanvas.LoadOtocadFile(legacy + ".json");
        check(oldCanvas.GetAllEntities().All(e => !e.UseEngineeringSerif), "未声明新字体的旧图按原样打开");
        TemplatePipeline.Export(oldCanvas, legacy, ["dxf"]);
        var oldDxf = netDxf.DxfDocument.Load(legacy + ".dxf");
        check(oldDxf.Entities.All.OfType<netDxf.Entities.Text>().Any() &&
            oldDxf.Entities.All.OfType<netDxf.Entities.Text>().All(t => !t.Style.Name.StartsWith("OpticsSerif", StringComparison.Ordinal)),
            "旧图重新导出 DXF 保留原有文字样式");

        VerifyLegacyAction(oldCanvas.GetAllEntities(), output, check);

        AppContext.TryGetSwitch(SwitchName, out bool savedSwitch);
        try
        {
            AppContext.SetSwitch(SwitchName, true);
            string portable = Path.Combine(output, "随包字体");
            var portableCanvas = new CadCanvas();
            portableCanvas.LoadOtocadFile(stem + ".otocad");
            TemplatePipeline.Export(portableCanvas, portable, ["dxf", "pdf"]);
            VerifyDxf(portable, true, check);
        }
        finally { AppContext.SetSwitch(SwitchName, savedSwitch); }
    }

    private static void VerifyLegacyAction(IReadOnlyList<Entity> oldSheet, string output, Action<bool, string> check)
    {
        var window = new MainWindow();
        try
        {
            window.Show();
            var canvas = window.ActiveCanvas;
            var custom = new lcdb.Text("专用注字", new LitMath.Vector3(20, 80, 0), 2.8) { TextStyle = "Courier New" };
            canvas.LoadGeneratedSheet(oldSheet.Append(custom).ToList());
            FrameValueConfirmationValidation.Layout(window);
            var button = window.FindControl<Button>("SidebarAdoptSerif")!;
            bool FrameUsesSerif() => canvas.GetAllEntities().OfType<IsoLensDrawingFrame>().Single().UseEngineeringSerif;
            bool KeepsCustomFont() => canvas.GetAllEntities().OfType<lcdb.Text>().Any(text =>
                text.Value == custom.Value && text.TextStyle == "Courier New" && !text.UseEngineeringSerif);
            check(button.IsVisible && !FrameUsesSerif(), "旧图在图纸页可选择套用新版字体");
            FrameValueConfirmationValidation.Click(button);
            check(FrameUsesSerif() && KeepsCustomFont() && !button.IsVisible && canvas.UndoCount == 1, "一次套用图框与标注字体，保留手选字族");
            check(canvas.Undo() && !FrameUsesSerif() && KeepsCustomFont() && button.IsVisible, "一步撤销恢复旧字体和可用按钮");
            check(canvas.Redo() && FrameUsesSerif() && KeepsCustomFont() && !button.IsVisible, "重做再次应用新字体");
            string path = Path.Combine(output, "套用新版.otocad");
            canvas.SaveCurrentScene(path);
            var reopened = new CadCanvas();
            reopened.LoadOtocadFile(path);
            check(reopened.GetAllEntities().OfType<IsoLensDrawingFrame>().Single().UseEngineeringSerif &&
                reopened.GetAllEntities().OfType<lcdb.Text>().Single(text => text.Value == custom.Value).TextStyle == "Courier New",
                "保存重开继续保留套用结果及手选字族");
        }
        finally { window.Close(); }
    }
    private static (string Text, string Style, double Height)[] VerifyDxf(string stem, bool bundledOnly, Action<bool, string> check)
    {
        string name = Path.GetFileName(stem);
        var drawing = netDxf.DxfDocument.Load(stem + ".dxf");
        var captions = drawing.Entities.All.OfType<netDxf.Entities.Text>().ToArray();
        var cjk = captions.Where(t => t.Style.Name == "OpticsSerifCJK").ToArray();
        var latin = captions.Where(t => t.Style.Name == "OpticsSerifLatin").ToArray();
        check(cjk.Any(t => t.Value.Contains(Chinese)) && cjk.All(t => IsCjkFace(t.Style.FontFamilyName) || IsCjkFace(t.Style.FontFile)),
            name + " DXF 中文引用思源宋体");
        check(!bundledOnly || latin.Length == 0, name + " 缺少西文字体时可仅用随包字体");
        var western = latin.Length == 0 ? cjk : latin;
        check(western.Any(t => t.Value.Contains("Ra 0.8")) && latin.All(t => IsSerifFace(t.Style.FontFamilyName) || IsSerifFace(t.Style.FontFile)),
            name + " DXF 粗糙度标注引用可识别的字体");
        string all = string.Concat(captions.Select(t => t.Value));
        check(Symbols.Where(c => c != ' ').All(c => all.Contains(c)), name + " DXF 保留直径、公差、希腊字母、无穷和角度符号");
        return captions.Select(t => (t.Value, t.Style.Name, t.Height)).ToArray();
    }

    private static bool IsCjkFace(string? name) =>
        name?.Replace(" ", "").Contains("SourceHanSerifSC", StringComparison.OrdinalIgnoreCase) == true ||
        name?.Contains("思源宋体", StringComparison.Ordinal) == true;

    private static bool IsSerifFace(string? name) => IsCjkFace(name) ||
        name?.Replace(" ", "").Contains("AnthropicSerif", StringComparison.OrdinalIgnoreCase) == true;

    private static void RemoveFontProfile(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(kv => kv.Key).ToArray())
                if (key.Equals("UseEngineeringSerif", StringComparison.OrdinalIgnoreCase)) obj.Remove(key);
                else RemoveFontProfile(obj[key]);
        }
        else if (node is JsonArray array)
            foreach (var item in array) RemoveFontProfile(item);
    }
}