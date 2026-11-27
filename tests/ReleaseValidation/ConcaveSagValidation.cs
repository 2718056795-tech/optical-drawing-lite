using System.Globalization;
using lcdb;
using lcdb.Annotation;
using lcdb.DrawingFrame;
using lcdb.Optic;
using OtoCAD.Avalonia;
using OtoCAD.Avalonia.Slim;

internal static class ConcaveSagValidation
{
    private const string Front = "front-sag";
    private const string Contact = "contact-sag";
    private const string Back = "back-sag";

    internal static void Run(string library, string output, OpticalLens codeV, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        var database = new Database();
        database.Open(Path.Combine(library, "默认模板.otocad"));
        var template = database.GetEntities().ToList();

        List<Entity> Draw(Entity part, string name) => TemplatePipeline.Build(template, part, name, "SAG-QA", "fixture");
        var v49 = Draw(codeV, "CODE V 月牙透镜");
        VerifyRoles(v49, check, Back);
        VerifySag(v49, Back, 87.1, 5, check);

        var lens = new OpticalLens
        {
            Diameter = 12,
            MechanicalDiameter = 16,
            Thickness = 5,
            FrontSurface = new SphericalSurface { Radius = -32, SemiAperture = 4, CurveSemiAperture = 6 },
            BackSurface = new SphericalSurface { Radius = 37, SemiAperture = 5.5, CurveSemiAperture = 7 }
        };
        var single = Draw(lens, "双凹与曲面延伸区");
        VerifyRoles(single, check, Front, Back);
        VerifySag(single, Front, 32, 4, check);
        VerifySag(single, Back, 37, 5.5, check);

        check(TemplatePipeline.TrySetDirectParameter(single, "BackAperture", 9.5, out var error),
            "编辑后表面通光口径：" + error);
        VerifySag(single, Back, 37, 4.75, check);
        check(TemplatePipeline.TrySetDirectParameter(single, "R2", 41, out error), "编辑后表面曲率半径：" + error);
        VerifySag(single, Back, 41, 4.75, check);
        VerifySag(single, Front, 32, 4, check);
        check(TemplatePipeline.TrySetDirectParameter(single, "R2", -41, out error), "将后面改为凸球面：" + error);
        VerifyRoles(single, check, Front);
        check(TemplatePipeline.TrySetDirectParameter(single, "R2", 41, out error), "将后面恢复为凹球面：" + error);
        VerifyRoles(single, check, Front, Back);
        VerifySag(single, Back, 41, 4.75, check);

        var glue = new CementedLens
        {
            Diameter = 12, T1 = 4, T2 = 5,
            FrontSurface = new SphericalSurface { Radius = -26, SemiAperture = 5 },
            ContactSurface = new SphericalSurface { Radius = -39, SemiAperture = 4 },
            BackSurface = new SphericalSurface { Radius = 35, SemiAperture = 6 }
        };
        var doublet = Draw(glue, "胶合矢高");
        VerifyRoles(doublet, check, Front, Contact, Back);
        VerifySag(doublet, Front, 26, 5, check);
        VerifySag(doublet, Contact, 39, 4, check);
        VerifySag(doublet, Back, 35, 6, check);

        lens.FrontSurface = new AsphericSurface { Radius = -32, SemiAperture = 4, ConicConstant = -1 };
        lens.BackSurface = OpticalSurface.Flat();
        var mixed = Draw(lens, "非球面与平面");
        VerifyRoles(mixed, check);
        check(mixed.OfType<AsphericDataBlock>().Count() == 1, "非球面保留一个矢高数据块");

        var canvas = new CadCanvas();
        canvas.LoadGeneratedSheet(v49);
        string stem = Path.Combine(output, "CODE-V凹面矢高");
        TemplatePipeline.Export(canvas, stem, ["otocad", "json", "pdf", "dxf", "png"]);
        foreach (string format in new[] { "otocad", "json" })
        {
            var saved = new Database();
            saved.Open(stem + "." + format);
            var entities = saved.GetEntities().ToList();
            VerifyRoles(entities, check, Back);
            VerifySag(entities, Back, 87.1, 5, check);
        }
        var dxf = netDxf.DxfDocument.Load(stem + ".dxf");
        check(dxf.Entities.All.OfType<netDxf.Entities.Text>().Any(t => t.Value.Contains("0.144", StringComparison.Ordinal)),
            "DXF 可读回矢高数值 0.144");
        check(new FileInfo(stem + ".pdf").Length > 1024 && new FileInfo(stem + ".png").Length > 1024,
            "带矢高的图纸可导出 PDF 和 PNG");
    }

    private static void VerifyRoles(IReadOnlyList<Entity> entities, Action<bool, string> check, params string[] expected)
    {
        var roles = entities.OfType<LinearDimension>().Select(d => d.OpticalDimensionRole)
            .Where(role => role is Front or Contact or Back).Order().ToArray();
        check(roles.SequenceEqual(expected.Order()), "自动生成正确的凹球面矢高角色 " + string.Join(", ", expected));
    }

    private static void VerifySag(IReadOnlyList<Entity> entities, string role, double radius, double semiAperture,
        Action<bool, string> check)
    {
        var owner = entities.Single(e => e is OpticalLens or CementedLens);
        var dimension = entities.OfType<LinearDimension>().Single(d => d.OpticalDimensionRole == role);
        double expected = semiAperture * semiAperture /
            (radius + Math.Sqrt(radius * radius - semiAperture * semiAperture));
        string number = expected.ToString("F3", CultureInfo.InvariantCulture);
        check(dimension.userText.Contains("矢高", StringComparison.Ordinal) &&
            dimension.userText.Contains(number, StringComparison.Ordinal), role + " 标注匹配该面通光半径数值 " + number);
        check(Math.Abs(dimension.measurement / owner.DrawingBinding!.ViewScale - expected) < 1e-7,
            role + " 尺寸两端的轴向投影按纸面比例对应真实矢高");
        check(Math.Abs(Math.Abs(dimension.secondReferencePoint.Y - dimension.firstReferencePoint.Y) /
            owner.DrawingBinding.ViewScale - semiAperture) < 1e-7, role + " 尺寸引自表面顶点和通光边缘");
    }
}
