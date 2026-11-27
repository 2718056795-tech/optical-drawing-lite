using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using lcdb.Rendering;
using OtoCAD.Avalonia;
using OtoCAD.Avalonia.Slim;

internal static class PrintedThicknessValidation
{
    internal static void Run(string library, string output, Action<bool,string> check)
    {
        var db = new Database();
        db.Open(Path.Combine(library, "默认模板.otocad"));
        var template = db.GetEntities().ToList();
        var region = template.OfType<TemplateViewRegion>().Single();
        region.UseFullPaperArea = false;
        region.ViewScale = 2;
        foreach (var (thickness, side) in new[] { (.6,-1), (16.0,0) })
        {
            var single = new OpticalLens{Diameter=12, Thickness=thickness, R1=500, R2=500};
            single.FrontSurface.SemiAperture=single.BackSurface.SemiAperture=6;
            var sheet = TemplatePipeline.Build(template, single, "单片厚度核验", "L1","fixture.zmx");
            var dim = sheet.OfType<LinearDimension>().Single(d =>
                TemplatePipeline.GetDirectParameterForAnnotation(sheet,d)?.Key == "Thickness");
            string name=side==0?"厚片居中":"薄片箭外";
            Verify(dim, side, name, check);
            var c=new CadCanvas(); c.LoadGeneratedSheet(sheet);
            TemplatePipeline.Export(c,Path.Combine(output,name),["pdf"]);
            if (side==0)
            {
                dim.TextHeightOverride=15;
                dim.Update();
                Verify(dim,-1,"原先宽裕的厚度手工采用大字号后",check);
            }
        }
        var glue = new CementedLens { Diameter=12, T1=.6, T2=.8, FrontSurface=OpticalSurface.Sphere(500),
            ContactSurface=OpticalSurface.Sphere(500), BackSurface=OpticalSurface.Sphere(500) };
        glue.FrontSurface.SemiAperture=glue.ContactSurface.SemiAperture=glue.BackSurface.SemiAperture=6;
        var cemented=TemplatePipeline.Build(template,glue,"胶合厚度核验","L2","fixture.zmx");
        foreach(var d in cemented.OfType<LinearDimension>())
        {
            var key=TemplatePipeline.GetDirectParameterForAnnotation(cemented,d)?.Key;
            if(key is "T1" or "T2") Verify(d,key=="T1"?-1:1,"胶合"+key,check);
        }
        // The total may fit between its end arrows while the interface witness intersects its middle.
        glue.T1=glue.T2=7.2;
        var broad=TemplatePipeline.Build(template,glue,"胶合总厚避让","L3","fixture.zmx");
        var total=broad.OfType<LinearDimension>().Single(d=>Math.Min(d.FirstAnchorIndex,d.SecondAnchorIndex)==1 && Math.Max(d.FirstAnchorIndex,d.SecondAnchorIndex)==2);
        var half=broad.OfType<LinearDimension>().Single(d=>TemplatePipeline.GetDirectParameterForAnnotation(broad,d)?.Key=="T1");
        var drawing=new DxfGraphicsDraw();
        total.Draw(drawing);
        var caption=drawing.Entities.OfType<netDxf.Entities.Text>().Single();
        double contact=Math.Max(half.firstReferencePoint.X,half.secondReferencePoint.X);
        double width=PaperTextLayout.Width(caption.Value,caption.Height);
        double span=Math.Abs(total.secondReferencePoint.X-total.firstReferencePoint.X);
        check(span>width+2*total.style.ArrowSize &&
            (caption.Position.X+width<contact-.2 || caption.Position.X>contact+.2),
            "胶合总厚跨度可容纳文字时也避开中间界线");
        var broadCanvas=new CadCanvas(); broadCanvas.LoadGeneratedSheet(broad);
        TemplatePipeline.Export(broadCanvas,Path.Combine(output,"胶合总厚避界线"),["pdf"]);
        var sheetCanvas=new CadCanvas(); sheetCanvas.LoadGeneratedSheet(cemented);
        TemplatePipeline.Export(sheetCanvas,Path.Combine(output,"胶合箭外"),["otocad","pdf"]);
        var again=new CadCanvas(); again.LoadOtocadFile(Path.Combine(output,"胶合箭外.otocad"));
        var reopened=again.GetAllEntities();
        Verify(reopened.OfType<LinearDimension>().Single(d=>TemplatePipeline.GetDirectParameterForAnnotation(reopened,d)?.Key=="T2"),
            1,"保存重开的胶合 T2",check);
    }

    private static void Verify(LinearDimension dimension, int expectedSide, string name, Action<bool,string> check)
    {
        const double tolerance=.01;
        var graphics=new DxfGraphicsDraw();
        dimension.Draw(graphics);
        var label=graphics.Entities.OfType<netDxf.Entities.Text>().Single();
        var left=Math.Min(dimension.firstReferencePoint.X,dimension.secondReferencePoint.X);
        var right=Math.Max(dimension.firstReferencePoint.X,dimension.secondReferencePoint.X);
        var x=label.Position.X;
        var y=dimension.dimLinePosition.Y;
        var horizontal=graphics.Entities.OfType<netDxf.Entities.Line>()
            .Where(l=>Math.Abs(l.StartPoint.Y-y)<tolerance && Math.Abs(l.EndPoint.Y-y)<tolerance)
            .Select(l=>(Lo:Math.Min(l.StartPoint.X,l.EndPoint.X),Hi:Math.Max(l.StartPoint.X,l.EndPoint.X))).ToArray();
        if(expectedSide==0)
            check(x>left && x<(left+right)/2 && Math.Abs(dimension.textReferencePoint.X-(left+right)/2)<tolerance && horizontal.All(l=>l.Lo>=left-tolerance && l.Hi<=right+tolerance),
                name+" 字能放下时保留在箭头中间");
        else if(expectedSide<0)
            check(x<left && horizontal.Any(l=>l.Lo<x-tolerance && l.Hi>=left-tolerance),name+" 数字在左箭头外，轴向支线从箭头连接至文字下方");
        else
            check(x>right && horizontal.Any(l=>l.Lo<=right+tolerance && l.Hi>x+tolerance),name+" 数字在右箭头外，轴向支线从箭头连接至文字下方");
    }
}