using Avalonia;
using lcdb;
using lcdb.Optic;
using lcdb.Optic.Import;
using OtoCAD.Avalonia;
using OtoCAD.Avalonia.Services;
using OtoCAD.Avalonia.Slim;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args[0] == "--data-paths") return ValidateDataPaths(args[1], args[2]);
        string output = Path.GetFullPath(args[0]);
        string sourceRoot = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("OPTICAL_DRAWING_LITE_DATA_DIR", Path.Combine(output, "user-data"));
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        var log = new List<string>();
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            log.Add("PASS " + description);
        }
        try
        {
            AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
            lcdb.Standards.DrawingConventionService.StoreDirOverride = Path.Combine(AppDataPaths.DataDirectory, "standards");
            Check(AppDataPaths.TemplatesDirectory.StartsWith(output, StringComparison.OrdinalIgnoreCase), "模板写入指定用户数据目录");
            Check(AppDataPaths.AutoSaveDirectory.StartsWith(output, StringComparison.OrdinalIgnoreCase), "恢复备份写入指定用户数据目录");
            Check(AppDataPaths.ImportReportsDirectory.StartsWith(output, StringComparison.OrdinalIgnoreCase), "导入报告写入指定用户数据目录");

            var parts = ImportParts(sourceRoot, Check);
            ImportWorkflowValidation.Run(sourceRoot, output, Check);
            int sheetNumber = VerifyExports(sourceRoot, output, parts, Check);
            FileOutputChecks.Run(Path.Combine(output, "export"), Check);
            log.AddRange(V113TemplateChecks.Run(Path.Combine(output, "template-113")));
            log.AddRange(DocumentSafetyValidation.Run(Path.Combine(output, "document-safety")));
            File.WriteAllLines(Path.Combine(output, "validation.txt"), log);
            Console.WriteLine($"PASS {log.Count} release checks; {sheetNumber} drawings; {sheetNumber * 5} exports");
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("FAIL " + ex);
            File.WriteAllLines(Path.Combine(output, "validation.txt"), log);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static List<Entity> ImportParts(string sourceRoot, Action<bool, string> Check)
    {
        var parts = new List<Entity>();
        foreach (string fixture in new[] { "cooke_triplet.zmx", "achromat_doublet.zmx" })
        {
            var data = new ZmxParser().Parse(Path.Combine(sourceRoot, "src", "lcdb.Tests", "fixtures", "zmx", fixture));
            Check(data.IsValid, "官方处方解析 " + fixture);
            foreach (var surface in data.Surfaces)
                if (surface.SemiDiameter <= 0) surface.SemiDiameter = 5; // Explicit test aperture, not a measured design value.
            var built = new PrescriptionToLensBuilder().Build(data);
            Check(built.Entities.Count > 0 && built.SkippedCount == 0, "官方处方完整构建 " + fixture);
            parts.AddRange(built.Entities);
        }
        parts.Add(new OpticalLens
        {
            Diameter = 12, Thickness = 4,
            FrontSurface = new AsphericSurface { Radius = 38, ConicConstant = -1.1, EvenCoefficients = new[] { 1.1e-6, -2e-9 } },
            BackSurface = OpticalSurface.Sphere(-55),
        });
        var codeV = new CodeVSeqParser().ParseText("RDM;LEN\nDIM M\nSO 0 1e13\nS 21.127 1.54 HLAK53B_CDGM\nCIR 5\nS 87.1 2\nCIR 5\nSI 0 0\nGO");
        Check(codeV.IsValid, "CODE V 静态处方解析");
        var codeVParts = new PrescriptionToLensBuilder().Build(codeV);
        Check(codeVParts.Entities.Count == 1, "CODE V 单片构建");
        var codeVLens = (OpticalLens)codeVParts.Entities.Single();
        Check(codeVLens.MaterialName == "H-LAK53B" && Math.Abs(codeVLens.RefractiveIndex - 1.755) < 1e-9, "CODE V CDGM目录材料匹配");
        parts.Add(codeVLens);
        Check(parts.OfType<CementedLens>().Any(), "覆盖双胶合透镜");
        return parts;
    }

    private static int VerifyExports(string sourceRoot, string output, List<Entity> parts, Action<bool, string> Check)
    {
        var templateDatabase = new Database();
        templateDatabase.Open(Path.Combine(sourceRoot, "src", "OtoCAD.Avalonia", "Templates", "默认模板.otocad"));
        var template = templateDatabase.GetEntities().ToList();
        var formats = new[] { "pdf", "dxf", "png", "otocad", "json" };
        int sheetNumber = 0;
        foreach (var part in parts)
        {
            sheetNumber++;
            var sheet = TemplatePipeline.Build(template, part, "发布核验" + sheetNumber, "TEST-" + sheetNumber, "official-fixture");
            var canvas = new CadCanvas();
            canvas.LoadGeneratedSheet(sheet);
            string stem = Path.Combine(output, "drawing-" + sheetNumber);
            TemplatePipeline.Export(canvas, stem, formats);
            foreach (string format in formats)
                Check(new FileInfo(stem + "." + format).Length > 100, $"图纸 {sheetNumber} 导出 {format}");
            foreach (string format in new[] { "otocad", "json" })
            {
                var reopened = new Database();
                reopened.Open(stem + "." + format);
                Check(reopened.GetEntities().Count() == sheet.Count, $"图纸 {sheetNumber} {format} 实体完整读回");
                Check(reopened.GetEntities().Count(entity => entity is OpticalLens or CementedLens) == 1, $"图纸 {sheetNumber} {format} 光学件读回");
            }
            Check(netDxf.DxfDocument.Load(stem + ".dxf").Entities.All.Any(), $"图纸 {sheetNumber} DXF可重新读取");
        }
        return sheetNumber;
    }

    private static int ValidateDataPaths(string output, string scenario)
    {
        output = Path.GetFullPath(output);
        string app = Path.Combine(output, "V1.12.0", "程序");
        string legacy = scenario == "side-by-side" ? Path.Combine(output, "V1.11.2", "程序") : app;
        Directory.CreateDirectory(Path.Combine(legacy, "Templates", "Assets"));
        Directory.CreateDirectory(Path.Combine(legacy, "Data"));
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(legacy, "Templates", "用户模板.otocad"), "legacy-template");
        File.WriteAllText(Path.Combine(legacy, "Templates", "Assets", "logo.txt"), "legacy-logo");
        File.WriteAllText(Path.Combine(legacy, "Data", "template-choice.json"), "legacy-choice");
        string data = Path.Combine(output, "user");
        if (scenario == "portable")
        {
            File.WriteAllText(Path.Combine(app, "portable.flag"), "");
            data = app;
            Environment.SetEnvironmentVariable("OPTICAL_DRAWING_LITE_DATA_DIR", null);
        }
        else
        {
            Environment.SetEnvironmentVariable("OPTICAL_DRAWING_LITE_DATA_DIR", data);
            Directory.CreateDirectory(Path.Combine(data, "Templates"));
            if (scenario == "existing")
                File.WriteAllText(Path.Combine(data, "Templates", "用户模板.otocad"), "newer-user-template");
            if (scenario == "completed") File.WriteAllText(Path.Combine(data, ".migration-complete"), "done");
        }
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", app + Path.DirectorySeparatorChar);
        if (!string.Equals(Path.TrimEndingDirectorySeparator(AppDataPaths.RootDirectory), data, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("User data root mismatch");
        string template = Path.Combine(AppDataPaths.TemplatesDirectory, "用户模板.otocad");
        if (scenario == "completed")
        {
            if (File.Exists(template)) throw new InvalidOperationException("Completed migration resurrected a removed template");
        }
        else
        {
            string expected = scenario == "existing" ? "newer-user-template" : "legacy-template";
            if (File.ReadAllText(template) != expected) throw new InvalidOperationException("Migration overwrote or lost user template");
            if (!File.Exists(Path.Combine(AppDataPaths.TemplatesDirectory, "Assets", "logo.txt")))
                throw new InvalidOperationException("Template assets missing");
            if (File.ReadAllText(Path.Combine(AppDataPaths.DataDirectory, "template-choice.json")) != "legacy-choice")
                throw new InvalidOperationException("Default template setting not migrated");
        }
        if (File.ReadAllText(Path.Combine(legacy, "Templates", "用户模板.otocad")) != "legacy-template")
            throw new InvalidOperationException("Legacy user template was modified");
        Console.WriteLine("PASS data path migration " + scenario);
        return 0;
    }
}
