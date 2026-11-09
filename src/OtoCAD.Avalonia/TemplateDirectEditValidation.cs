using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using LitMath;

namespace OtoCAD.Avalonia.Slim;

public static class TemplateDirectEditValidation
{
    public static IReadOnlyList<string> Run(string output)
    {
        Directory.CreateDirectory(output);
        var log = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Direct edit test: " + message);
            log.Add("PASS " + message);
        }
        void Set(IReadOnlyList<Entity> sheet, string key, double value)
        {
            Check(TemplatePipeline.TrySetDirectParameter(sheet, key, value, out var error), key + " 写入真实数值 " + value.ToString(CultureInfo.InvariantCulture) + "; " + error);
        }
        static OpticalLens Source(List<Entity> sheet) => (OpticalLens)TemplatePipeline.SourcePartFromView(sheet.OfType<OpticalLens>().First());
        static string State(List<Entity> sheet) => string.Join("\n", sheet.Select(e => e switch
        {
            OpticalLens l => $"lens:{l.R1:R},{l.R2:R},{l.Thickness:R},{l.Diameter:R},{l.MechanicalDiameter:R},{l.FrontSurface.SemiAperture:R},{l.BackSurface.SemiAperture:R},{l.DrawingBinding?.LastGeometrySignature}",
            CementedLens d => $"doublet:{d.R1:R},{d.RContact:R},{d.R3:R},{d.T1:R},{d.T2:R},{d.Diameter:R},{d.MechanicalDiameter:R},{d.DrawingBinding?.LastGeometrySignature}",
            TemplateTable t => string.Join("|", t.Columns.SelectMany(c => c.Rows)) + string.Join("|", t.TemplateTextBindings?.Select(b => b.Value.Expression + b.Value.LastRendered) ?? Enumerable.Empty<string>()),
            LinearDimension d => $"dim:{d.userText}:{d.LastAutomaticText}:{d.firstReferencePoint}:{d.secondReferencePoint}",
            Text t => t.Value,
            _ => e.className
        }));
        List<Entity> Template(bool multi = false)
        {
            var result = TemplatePipeline.Preset("Blank");
            var first = result.OfType<TemplateViewRegion>().Single();
            first.ViewScale = 2;
            if (multi)
            {
                var other = (TemplateViewRegion)first.Clone(); other.ViewScale = 3; other.Origin = new Vector2(90, 100); result.Add(other);
            }
            result.Add(new TemplateTable
            {
                Origin = new Vector2(20, 30), Columns = new()
                {
                    new IsoSpecColumn { Title = "可编辑设计值", Rows = new()
                    {
                        "厚度【中心厚度】 ±0.1", "前面【前表面半径】；再次【前表面半径】", "后面【后表面半径】",
                        "前面口径【前表面通光口径】", "后面口径【后表面通光口径】", "外径【机械外径】",
                        "第一片【片1厚度】", "第二片【片2厚度】", "胶合半径【胶合面半径】", "胶合口径【胶合面通光口径】",
                        "整体通光【通光口径】", "技术说明：保持倒角 0.15"
                    }}
                }
            });
            return result;
        }
        var source = new OpticalLens { R1 = 50, R2 = -60, Thickness = 4, Diameter = 10, MechanicalDiameter = 12 };
        source.ApplyMaterial("N-BK7");
        source.FrontSurface.SemiAperture = 5; source.BackSurface.SemiAperture = 4;
        var sheet = TemplatePipeline.Build(Template(), source, "edit-single", "DE1", "direct.zmx");
        var table = sheet.OfType<TemplateTable>().Single();
        var view = sheet.OfType<OpticalLens>().Single();
        Check(TemplatePipeline.GetDirectParametersForCell(sheet, table, 0, 0).Single() is { Key: "Thickness", Value: 4 }, "表格显示真实厚度，不显示放大后的图面尺寸");
        var radius = TemplatePipeline.GetDirectParametersForCell(sheet, table, 0, 1);
        Check(radius.Count == 1 && radius[0].Key == "R1" && radius[0].Value == 50 && radius[0].AllowInfinity, "重复半径占位符只提供一个带符号输入值");
        Check(TemplatePipeline.GetDirectParametersForCell(sheet, table, 0, 2).Single().Value == -60, "表格半径输入保留负号");
        Check(TemplatePipeline.GetDirectParametersForCell(sheet, table, 0, 10).Count == 0, "独立逐面口径存在时整体口径是派生值");
        Check(TemplatePipeline.GetDirectParametersForCell(sheet, table, 0, 11).Count == 0 && TemplatePipeline.GetDirectParametersForCell(sheet, table, 0, -1).Count == 0, "技术文字和列标题不猜测设计参数");
        table.Columns[0].Rows[0] += "（检验）";
        Set(sheet, "Thickness", 5);
        Check(view.Thickness == 10 && Source(sheet).Thickness == 5 && table.Columns[0].Rows[0] == "厚度5 ±0.1（检验）", "输入真值后图面、参数表与手工公差说明保持一致");
        var thicknessDim = sheet.OfType<LinearDimension>().Single(d => d.InferDrivingParameterKey(sheet) == "Thickness");
        var descriptor = TemplatePipeline.GetDirectParameterForAnnotation(sheet, thicknessDim);
        Check(descriptor is { Key: "Thickness", Value: 5 } && thicknessDim.userText.Contains("5.00"), "厚度标注解析宿主且随实际厚度更新");
        var manualCaption = sheet.OfType<LinearDimension>().FirstOrDefault(d => d.InferDrivingParameterKey(sheet) == "MechanicalDiameter");
        if (manualCaption is not null) manualCaption.userText += " +0.02";
        string before = State(sheet);
        foreach (var invalid in new[] { ("Thickness", -1.0), ("Thickness", double.NaN), ("Thickness", double.PositiveInfinity), ("R1", 0.0), ("R1", 2.0), ("R1", double.MaxValue), ("MechanicalDiameter", 9.0), ("FrontAperture", 13.0), ("Diameter", 9.0), ("Unknown", 2.0) })
        {
            Check(!TemplatePipeline.TrySetDirectParameter(sheet, invalid.Item1, invalid.Item2, out string error) && !string.IsNullOrWhiteSpace(error) && State(sheet) == before,
                "拒绝无效 " + invalid.Item1 + "=" + invalid.Item2.ToString(CultureInfo.InvariantCulture) + " 且所有视图、文字和标注未改变");
        }
        Set(sheet, "FrontAperture", 9);
        Check(Source(sheet).FrontSurface.SemiAperture == 4.5 && Source(sheet).Diameter == 9 && Source(sheet).MechanicalDiameter == 12 && table.Columns[0].Rows[3].Contains("9"), "逐面口径按直径输入、同步总口径且保留机械外径");
        Set(sheet, "MechanicalDiameter", 13);
        Check(Source(sheet).MechanicalDiameter == 13 && table.Columns[0].Rows[5] == "外径13", "机械外径可以直接修改并进入模板字段");
        if (manualCaption is not null) Check(manualCaption.userText.EndsWith(" +0.02", StringComparison.Ordinal), "保留人工标注的附加公差");
        Set(sheet, "R2", double.PositiveInfinity);
        Check(double.IsPositiveInfinity(Source(sheet).R2) && table.Columns[0].Rows[2].Contains("∞"), "半径设为平面同步图形与参数表");
        Set(sheet, "R1", -80);
        Check(Source(sheet).R1 == -80 && table.Columns[0].Rows[1].Contains("80.000"), "曲率正负号驱动实际曲面且表格按面方向显示");

        var multi = TemplatePipeline.Build(Template(true), source, "multi-edit", "DE2", "direct.zmx");
        var views = multi.OfType<OpticalLens>().ToArray();
        var positions = views.Select(v => v.Position).ToArray();
        Set(multi, "Thickness", 7);
        Check(views[0].Thickness == 14 && views[1].Thickness == 21 && views[0].Position.Equals(positions[0]) && views[1].Position.Equals(positions[1]), "多视图分别按 2 倍和 3 倍同步且保留位置");
        var dimensions = multi.OfType<LinearDimension>().Where(d => d.InferDrivingParameterKey(multi) == "Thickness").ToArray();
        Check(dimensions.Length == 2 && dimensions.All(d => TemplatePipeline.GetDirectParameterForAnnotation(multi, d)?.Value == 7 && d.userText.Contains("7.00")), "多视图标注按各自持久宿主更新真实值");
        var detached = (LinearDimension)dimensions[0].Clone(); detached.FirstAnchorKey = "missing"; multi.Add(detached);
        Check(TemplatePipeline.GetDirectParameterForAnnotation(multi, detached) is null, "失去宿主的尺寸不能驱动其他镜片");
        multi.Remove(detached);
        var legacy = dimensions[0]; legacy.DrivingParameterKey = "";
        Check(TemplatePipeline.GetDirectParameterForAnnotation(multi, legacy)?.Key == "Thickness", "旧版生成尺寸可保守恢复直接编辑入口");

        var glue = new CementedLens { R1 = 50, RContact = -80, R3 = -60, T1 = 3, T2 = 2, Diameter = 8, MechanicalDiameter = 9 };
        glue.FrontSurface.SemiAperture = 4; glue.ContactSurface.SemiAperture = 4; glue.BackSurface.SemiAperture = 4;
        glue.ApplyMaterial1("N-BK7"); glue.ApplyMaterial2("N-F2");
        var doublet = TemplatePipeline.Build(Template(), glue, "glue-edit", "DE3", "direct.seq");
        var gt = doublet.OfType<TemplateTable>().Single();
        Check(TemplatePipeline.GetDirectParametersForCell(doublet, gt, 0, 0).Count == 0 && TemplatePipeline.GetDirectParametersForCell(doublet, gt, 0, 6).Single().Key == "T1" && TemplatePipeline.GetDirectParametersForCell(doublet, gt, 0, 7).Single().Key == "T2", "胶合总厚为派生值，分别提供两片中心厚度");
        Set(doublet, "T1", 4); Set(doublet, "T2", 3);
        var gd = (CementedLens)TemplatePipeline.SourcePartFromView(doublet.OfType<CementedLens>().Single());
        Check(gd.T1 == 4 && gd.T2 == 3 && gt.Columns[0].Rows[0] == "厚度7 ±0.1" && gt.Columns[0].Rows[6] == "第一片4" && gt.Columns[0].Rows[7] == "第二片3", "胶合双层厚度、总厚和参数表同步");
        Check(!TemplatePipeline.TrySetDirectParameter(doublet, "Thickness", 8, out _) && ((CementedLens)TemplatePipeline.SourcePartFromView(doublet.OfType<CementedLens>().Single())).T1 == 4, "不根据胶合总厚猜测两片厚度分配");
        Check(doublet.OfType<LinearDimension>().Where(d => d.userText.StartsWith("CT", StringComparison.Ordinal)).All(d => TemplatePipeline.GetDirectParameterForAnnotation(doublet, d) is null), "胶合总厚标注不可反向驱动");
        Set(doublet, "ContactAperture", 7);
        Check(((CementedLens)TemplatePipeline.SourcePartFromView(doublet.OfType<CementedLens>().Single())).ContactSurface.SemiAperture == 3.5, "胶合面通光口径按直径修改");

        foreach (string extension in new[] { "otocad", "json" })
        {
            string path = Path.Combine(output, "直接参数编辑重开." + extension);
            MainWindow.SaveTemplateEntities(multi, path); var db = new Database(); db.Open(path); var reopened = db.GetEntities().ToList();
            var reopenedDim = reopened.OfType<LinearDimension>().First(d => d.InferDrivingParameterKey(reopened) == "Thickness");
            Check(TemplatePipeline.GetDirectParameterForAnnotation(reopened, reopenedDim)?.Value == 7, "保存重开保留标注与真值关联 " + extension);
            Set(reopened, "Thickness", 6);
            Check(reopened.OfType<OpticalLens>().All(v => Math.Abs(((OpticalLens)TemplatePipeline.SourcePartFromView(v)).Thickness - 6) < 1e-9) && reopened.OfType<TemplateTable>().Single().Columns[0].Rows[0] == "厚度6 ±0.1", "保存重开仍可编辑全部视图与参数表 " + extension);
        }
        var noFaces = new OpticalLens { R1 = 80, R2 = -80, Thickness = 4, Diameter = 8 };
        noFaces.ApplyMaterial("N-BK7");
        var overallSheet = TemplatePipeline.Build(Template(), noFaces, "overall", "DE4", "direct.zmx");
        Set(overallSheet, "Diameter", 9);
        Check(Source(overallSheet).Diameter == 9, "没有独立逐面口径时支持整体通光口径驱动");
        Set(overallSheet, "MechanicalDiameter", 10);
        Check(Source(overallSheet).MechanicalDiameter == 10, "没有原机械外径时可明确建立机械外径");
        var asphereSource = new OpticalLens { R2 = -80, Thickness = 4, Diameter = 8 };
        asphereSource.FrontSurface = new AsphericSurface { Radius = 70, SemiAperture = 4, ConicConstant = -0.8, QuadraticCoefficient = 0.0001, EvenCoefficients = new[] { 0.000001, -0.00000001 } };
        asphereSource.BackSurface = new AsphericSurface { Radius = -80, SemiAperture = 3.5, ConicConstant = -1.2, QuadraticCoefficient = 0.0002, EvenCoefficients = new[] { 0.000002 } };
        asphereSource.ApplyMaterial("N-BK7");
        var asphereSheet = TemplatePipeline.Build(Template(true), asphereSource, "asphere", "DE5", "direct.zmx");
        Set(asphereSheet, "R1", 90); Set(asphereSheet, "R2", -95);
        Check(asphereSheet.OfType<OpticalLens>().All(v =>
        {
            var s = (OpticalLens)TemplatePipeline.SourcePartFromView(v);
            return s.FrontSurface is AsphericSurface f && s.BackSurface is AsphericSurface b && f.Radius == 90 && b.Radius == -95 && f.SemiAperture == 4 && b.SemiAperture == 3.5 && f.ConicConstant == -0.8 && b.ConicConstant == -1.2 &&
                Math.Abs(f.QuadraticCoefficient - 0.0001) < 1e-15 && Math.Abs(b.QuadraticCoefficient - 0.0002) < 1e-15 && f.EvenCoefficients.Length == 2 && Math.Abs(f.EvenCoefficients[0] - 0.000001) < 1e-15 && Math.Abs(f.EvenCoefficients[1] + 0.00000001) < 1e-15 && b.EvenCoefficients.Length == 1;
        }), "修改非球面基础半径保留面型、A2、高次项、锥系数及逐面口径");
        var a2block = asphereSheet.OfType<lcdb.Annotation.AsphericDataBlock>().First(b => b.SurfaceLabel == "1");
        Check(a2block.BaseRadius == 90 && Math.Abs(a2block.QuadraticCoefficient - 0.0001) < 1e-15, "非球面数据块同步新半径并保留真实系数");
        return log;
    }
}

