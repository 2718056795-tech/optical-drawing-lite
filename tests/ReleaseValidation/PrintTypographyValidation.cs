using System.Text.Json;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using lcdb.Rendering;
using OtoCAD.Avalonia;
using OtoCAD.Avalonia.Slim;

internal static class PrintTypographyValidation
{
    private static readonly string[] Names = ["默认模板", "ISO三列表", "中文内部图", "GB-my-1"];

    internal static void Run(string library, string output, Entity single, Entity glue, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        foreach (var name in Names)
        {
            var db = new Database();
            db.Open(Path.Combine(library, name + ".otocad"));
            foreach (var (part, kind) in new[] { (single, "单片"), (glue, "胶合") })
            {
                var sheet = TemplatePipeline.Build(db.GetEntities().ToList(), part, "元件1 H-LAK53B [S1/S2]", "L-001", "sample.zmx");
                string stem = Path.Combine(output, name + "-" + kind);
                VerifyLayout(sheet, stem, check);
            }
        }
        var custom = new Database();
        custom.Open(Path.Combine(library, "默认模板.otocad"));
        var customFrame = custom.GetEntities().OfType<IsoLensDrawingFrame>().Single();
        customFrame.SpecificationBodyHeight = 2.17;
        var own = new CadCanvas();
        own.LoadGeneratedSheet(custom.GetEntities().ToList());
        string path = Path.Combine(output,"自选字高.otocad");
        own.SaveCurrentScene(path);
        var reopen = new CadCanvas();
        reopen.LoadOtocadFile(path);
        var result = TemplatePipeline.Build(reopen.GetAllEntities(), single, "自定义", "L-002", "sample.zmx");
        check(result.OfType<IsoLensDrawingFrame>().Single().SpecificationBodyHeight == 2.17, "自存模板再打开出图时保持所选字号");
    }

    private static void VerifyLayout(List<Entity> sheet, string stem, Action<bool, string> check)
    {
        var canvas = new CadCanvas();
        canvas.LoadGeneratedSheet(sheet);
        var frame = sheet.OfType<DrawingFrame>().First(f => f is IPropertyZoneFrame && f is not TemplateTable);
        var zone = (IPropertyZoneFrame)frame;
        var cells = new List<object>();
        for (int col=0; zone.GetCell(col,0) is not null; col++)
            for (int row=0; zone.GetCell(col,row) is { } cell; row++)
                cells.Add(new { col, row, text=zone.GetCellText(cell), x=cell.Rect.location.X, y=cell.Rect.location.Y,
                    width=cell.Rect.width, height=cell.Rect.height });
        var labels = new List<object>();
        foreach (var dim in sheet.OfType<DimensionBase>())
        {
            var drawing = new DxfGraphicsDraw();
            dim.Draw(drawing);
            var glyph = drawing.Entities.OfType<netDxf.Entities.Text>().Single();
            var role = TemplatePipeline.GetDirectParameterForAnnotation(sheet,dim)?.Key ?? "measurement";
            check(glyph.Height >= 3.49, Path.GetFileName(stem) + " · " + role + " 实际输出纸面字高至少 3.5 mm");
            var linear = dim as LinearDimension;
            var stroke = drawing.Entities.OfType<netDxf.Entities.Line>()
                .Select(l => new[] {l.StartPoint.X,l.StartPoint.Y,l.EndPoint.X,l.EndPoint.Y}).ToArray();
            labels.Add(new { role, text=glyph.Value, height=glyph.Height, x=glyph.Position.X, y=glyph.Position.Y,
                x1=linear?.firstReferencePoint.X, x2=linear?.secondReferencePoint.X, lineY=linear?.dimLinePosition.Y, stroke });
        }
        VerifyRadiusClearance(sheet, Path.GetFileName(stem), check);
        var roughness = sheet.OfType<lcdb.Annotation.SurfaceRoughnessMark>().Select(mark =>
        {
            var drawing = new DxfGraphicsDraw();
            mark.Draw(drawing);
            return drawing.Entities.OfType<netDxf.Entities.Text>().Single(t => t.Value.StartsWith("Ra",StringComparison.Ordinal));
        }).ToArray();
        check(roughness.Length==2 && roughness.All(t=>Math.Abs(t.Height-3)<.001),
            Path.GetFileName(stem)+" 两个镜面粗糙度 Ra 0.8 均以 3 mm 输出");
        check(cells.Count>0 && labels.Count>=3,Path.GetFileName(stem) + " 含参数表与尺寸标注");
        TemplatePipeline.Export(canvas,stem,["pdf"]);
        File.WriteAllText(stem+".layout.json", JsonSerializer.Serialize(new {frame.PaperWidth,frame.PaperHeight,cells,labels}));
    }
    private static void VerifyRadiusClearance(List<Entity> sheet, string name, Action<bool,string> check)
    {
        var diameters=sheet.OfType<LinearDimension>().Where(d=>
            TemplatePipeline.GetDirectParameterForAnnotation(sheet,d)?.Key is "Diameter" or "MechanicalDiameter");
        var strokes=new DxfGraphicsDraw();
        foreach(var diameter in diameters) diameter.Draw(strokes);
        var verticals=strokes.Entities.OfType<netDxf.Entities.Line>()
            .Where(l=>Math.Abs(l.StartPoint.X-l.EndPoint.X)<.01).ToArray();
        foreach(var radius in sheet.OfType<RadialDimension>())
        {
            var output=new DxfGraphicsDraw(); radius.Draw(output);
            var caption=output.Entities.OfType<netDxf.Entities.Text>().Single();
            double left=caption.Position.X, right=left+PaperTextLayout.Width(caption.Value,caption.Height,radius.UseEngineeringSerif);
            double y=caption.Position.Y+caption.Height*.5;
            bool crosses=verticals.Any(l=>l.StartPoint.X>left+.2 && l.StartPoint.X<right-.2 &&
                Math.Min(l.StartPoint.Y,l.EndPoint.Y)<y && Math.Max(l.StartPoint.Y,l.EndPoint.Y)>y);
            check(!crosses,name+" 半径数字 "+caption.Value+" 避开通光或机械外径的竖直尺寸线");
        }
    }
}