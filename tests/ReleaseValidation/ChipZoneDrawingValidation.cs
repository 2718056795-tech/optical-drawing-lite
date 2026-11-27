using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using OtoCAD.Avalonia;
using OtoCAD.Avalonia.Commands;
using OtoCAD.Avalonia.Dialogs;
using OtoCAD.Avalonia.PropertyPanel;
using OtoCAD.Avalonia.Slim;
using SkiaSharp;

internal static class ChipZoneDrawingValidation
{
    public static void Run(MainWindow window, string output, Action<bool,string> check)
    {
        Directory.CreateDirectory(output);
        string path = Path.Combine(output, "chip-zone.zmx");
        File.WriteAllText(path, Prescription);
        var before = window.SidebarDocumentTabs.ToArray();
        Task importing = PartialImportValidation.ImportFile(window, path);
        PartialImportValidation.Wait(() => window.OwnedWindows.Any());
        if (window.OwnedWindows.Single() is not ImportSelectionDialog)
        {
            var notice = window.OwnedWindows.Single();
            notice.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "确定"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PartialImportValidation.Wait(() => window.OwnedWindows.OfType<ImportSelectionDialog>().Any());
        }
        var dialog = window.OwnedWindows.OfType<ImportSelectionDialog>().Single();
        var preview = dialog.FindControl<CadCanvas>("PreviewCanvas")!.GetAllEntities();
        CheckFaces(preview.OfType<OpticalLens>().Single(), check);
        CheckFaces(preview.OfType<CementedLens>().Single(), check);
        using (var image = new RenderTargetBitmap(new PixelSize((int)dialog.ClientSize.Width, (int)dialog.ClientSize.Height)))
        {
            image.Render(dialog);
            image.Save(Path.Combine(output, "preview.png"));
        }
        var generate = dialog.FindControl<Button>("OkBtn")!;
        check(generate.IsEnabled, "带延伸区的单片和胶合件可直接生成");
        generate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PartialImportValidation.Wait(() => importing.IsCompleted);
        importing.GetAwaiter().GetResult();
        var added = window.SidebarDocumentTabs.Except(before).Select(t => (CadCanvas)t.Content!).ToArray();
        var sheets = added.Where(c => TemplatePipeline.IsGeneratedDrawing(c.GetAllEntities())).ToArray();
        check(sheets.Length == 2, "默认模板生成单片及双胶合各一张");
        foreach (var sheet in sheets) VerifySheet(sheet, output, check);
        VerifyReducedScale(preview.OfType<OpticalLens>().Single(), check);
        foreach (var part in preview) VerifyRadiusEditors(part, check);
    }

    private static void CheckFaces(Entity part, Action<bool,string> check)
    {
        var k = part.DrawingBinding?.ViewScale ?? 1;
        void Face(OpticalSurface face, double clear, double curve) =>
            check(Math.Abs(face.SemiAperture / k - clear) < 1e-9 &&
                  face.CurveSemiAperture is double limit && Math.Abs(limit / k - curve) < 1e-9,
                $"逐面通光/曲面终止半径 {clear}/{curve} mm 在 {k:g4} 倍视图中正确");
        if (part is OpticalLens l)
        {
            Face(l.FrontSurface, 5, 6); Face(l.BackSurface, 5, 7);
        }
        else
        {
            var d = (CementedLens)part;
            Face(d.FrontSurface, 6, 7); Face(d.ContactSurface, 4, 5.5); Face(d.BackSurface, 5, 6.25);
        }
    }

    private static void VerifySheet(CadCanvas sheet, string output, Action<bool,string> check)
    {
        var entities = sheet.GetAllEntities();
        Entity part = entities.Single(e => e is OpticalLens or CementedLens);
        bool single = part is OpticalLens;
        string stem = Path.Combine(output, single ? "single" : "doublet");
        CheckFaces(part, check);
        var values = TemplatePipeline.Values(TemplatePipeline.SourcePartFromView(part), "", "", "", "");
        check(values["前表面通光口径"] == (single ? "10" : "12") && values["后表面通光口径"] == "10" &&
              (single || values["胶合面通光口径"] == "8"), "模板中的通光参数取光学通光直径");
        check(!TemplatePipeline.TrySetDirectParameter(entities, "MechanicalDiameter", single ? 12 : 13, out var error) &&
              error.Contains("延伸区"), "编辑时拦截切入曲面延伸区的机械外径");
        check(TemplatePipeline.TrySetDirectParameter(entities, single ? "Thickness" : "T1", 6, out error),
            "在加工图中可直接修改厚度：" + error);
        CheckFaces(part, check);

        TemplatePipeline.Export(sheet, stem, ["otocad", "json", "png", "pdf", "dxf"]);
        foreach (string ext in new[]{"otocad","json"})
        {
            var reloaded = new CadCanvas();
            reloaded.LoadOtocadFile(stem + "." + ext);
            CheckFaces(reloaded.GetAllEntities().Single(e => e is OpticalLens or CementedLens), check);
        }
        var frame = entities.OfType<DrawingFrame>().First(x => x is not TemplateViewRegion && x is not TemplateTable);
        CheckExportedShape(stem, part, frame, check);
    }

    private static void CheckExportedShape(string stem, Entity part, DrawingFrame frame, Action<bool,string> check)
    {
        var l = part as OpticalLens; var d = part as CementedLens;
        var surface = l?.FrontSurface ?? d!.FrontSurface;
        var center = l?.Position ?? d!.Position;
        double apex = center.X - (l?.Thickness ?? d!.T1 + d.T2) / 2;
        double end = surface.CurveSemiAperture!.Value;
        double x = apex + surface.Sag(end);
        double rimY = center.Y + (l?.MechanicalDiameter ?? d!.MechanicalDiameter)!.Value / 2;
        double shoulderY = center.Y + end;
        var edge = AutoDimensionLensCmd.BuildEdgeThickness(part)!;
        check(Math.Abs(edge.firstReferencePoint.X - x) < 1e-7 && Math.Abs(edge.firstReferencePoint.Y - rimY) < 1e-7,
            "边厚尺寸线落在曲面终点矢高与机械外圆相交处");
        var document = netDxf.DxfDocument.Load(stem + ".dxf");
        bool Shoulder(netDxf.Vector3 a, netDxf.Vector3 b) => Math.Abs(a.X - x) < 1e-6 && Math.Abs(b.X - x) < 1e-6 &&
            Math.Abs(Math.Min(a.Y,b.Y) - shoulderY) < 1e-6 && Math.Abs(Math.Max(a.Y,b.Y) - rimY) < 1e-6;
        check(document.Entities.Lines.Any(line => Shoulder(line.StartPoint,line.EndPoint)),
            "DXF 平肩始于延伸区末端矢高，终点在机械外径");

        using var png = SKBitmap.Decode(stem + ".png");
        double h = (surface.SemiAperture + end) / 2;
        double scale = 200 / 25.4;
        int sampleX = (int)Math.Round((apex + surface.Sag(h) - frame.Origin.X) * scale);
        int sampleY = (int)Math.Round((frame.Origin.Y + frame.PaperHeight - center.Y - h) * scale);
        bool ink = Enumerable.Range(-2,5).Select(dx => png.GetPixel(sampleX + dx,sampleY)).Any(c => c.Red < 110 && c.Green < 110);
        check(ink, "PNG 在通光区外沿原面型矢高绘制轮廓");
        using var pdf = File.OpenRead(stem + ".pdf");
        var header = new byte[5];
        check(pdf.Read(header) == 5 && System.Text.Encoding.ASCII.GetString(header) == "%PDF-", "加工图 PDF 已输出");
    }

    private static void VerifyReducedScale(OpticalLens source, Action<bool,string> check)
    {
        var template = TemplatePipeline.Preset("ISO");
        template.OfType<TemplateViewRegion>().Single().ViewScale = .5;
        var sheet = TemplatePipeline.Build(template, source, "缩小视图", "C", "chip-zone.zmx");
        var displayed = sheet.OfType<OpticalLens>().Single();
        var physical = (OpticalLens)TemplatePipeline.SourcePartFromView(displayed);
        check(physical.MechanicalDiameter == 14 && physical.Diameter == 10, "缩小图还原原始通光口径与机械外径");
        CheckFaces(physical, check);
        var iso = IsoSingleLensSheet.Build(IsoSingleLensSheet.SpecFromLens(source)).OfType<OpticalLens>().Single();
        CheckFaces(iso, check);
    }

    private static void VerifyRadiusEditors(Entity design, Action<bool,string> check)
    {
        var view = TemplatePipeline.Build(TemplatePipeline.Preset("ISO"), design, "属性", "P", "chip-zone.zmx")
            .Single(e => e is OpticalLens or CementedLens);
        static AsphericSurface Asphere(OpticalSurface source) => new()
        {
            Radius = source.Radius, SemiAperture = source.SemiAperture, CurveSemiAperture = source.CurveSemiAperture,
            ConicConstant = -.8, QuadraticCoefficient = 1e-4, EvenCoefficients = [1e-6, -1e-9]
        };
        if (view is OpticalLens single)
        {
            single.FrontSurface = Asphere(single.FrontSurface); single.BackSurface = Asphere(single.BackSurface);
        }
        else if (view is CementedLens doublet)
        {
            doublet.FrontSurface = Asphere(doublet.FrontSurface);
            doublet.ContactSurface = Asphere(doublet.ContactSurface); doublet.BackSurface = Asphere(doublet.BackSurface);
        }
        var panel = new PropertyPanelControl();
        var host = new Window { Content = panel };
        try
        {
            host.Show();
            panel.SetEntity(view);
            Dispatcher.UIThread.RunJobs();
            void Edit(string label, OpticalSurface surface, Func<OpticalSurface> current)
            {
                var field = panel.GetLogicalDescendants().OfType<StackPanel>().Single(p =>
                    p.Children.OfType<TextBlock>().Any(t => t.Text == label) && p.Children.OfType<NumericUpDown>().Any());
                var edit = field.Children.OfType<NumericUpDown>().Single();
                double radius = surface.Radius, clear = surface.SemiAperture, curve = surface.CurveSemiAperture!.Value;
                var asphere = (AsphericSurface)surface;
                var terms = asphere.EvenCoefficients.ToArray();
                double a2 = asphere.QuadraticCoefficient, conic = asphere.ConicConstant, scale = view.DrawingBinding!.ViewScale;
                edit.Value = (decimal)(radius / scale + 2);
                check(ReferenceEquals(current(), surface) && Math.Abs(surface.Radius - radius - 2 * scale) < 1e-8 &&
                      surface.SemiAperture == clear && surface.CurveSemiAperture == curve && asphere.ConicConstant == conic &&
                      asphere.QuadraticCoefficient == a2 && asphere.EvenCoefficients.SequenceEqual(terms),
                    $"实体属性 {label} 修改实际半径后保留非球面、通光与延伸区");
            }
            if (view is OpticalLens l)
            {
                Edit("前表面 R1", l.FrontSurface, () => l.FrontSurface);
                Edit("后表面 R2", l.BackSurface, () => l.BackSurface);
            }
            else
            {
                var d = (CementedLens)view;
                Edit("前表面 R1", d.FrontSurface, () => d.FrontSurface);
                Edit("胶合面 Rc", d.ContactSurface, () => d.ContactSurface);
                Edit("后表面 R3", d.BackSurface, () => d.BackSurface);
            }
        }
        finally { host.Close(); }
    }

    private const string Prescription = """
        MODE SEQ
        UNIT MM
        WAVM 1 0.55 1
        SURF 0
         TYPE STANDARD
         DISZ INFINITY
        SURF 1
         TYPE STANDARD
         CURV 0.0625
         DISZ 5
         DIAM 5
         OEMA 1
         MEMA 7
         GLAS H-K9L 0 0 1.5 40
        SURF 2
         TYPE STANDARD
         CURV -0.025
         DISZ 6
         DIAM 5
         OEMA 2
         MEMA 7
        SURF 3
         TYPE STANDARD
         CURV 0.05
         DISZ 5
         DIAM 6
         OEMA 1
         MEMA 8
         GLAS H-LAK53B 0 0 1.5 40
        SURF 4
         TYPE STANDARD
         CURV 0.015
         DISZ 4
         DIAM 4
         OEMA 1.5
         MEMA 8
         GLAS H-K9L 0 0 1.5 40
        SURF 5
         TYPE STANDARD
         CURV -0.025
         DISZ 5
         DIAM 5
         OEMA 1.25
         MEMA 8
        SURF 6
         TYPE STANDARD
         DISZ 0
        """;
}
