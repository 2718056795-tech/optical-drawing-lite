using lcdb;
using lcdb.DrawingFrame;
using LitMath;
using OtoCAD.Avalonia;
using OtoCAD.Avalonia.Slim;
using SkiaSharp;

internal static class FileOutputChecks
{
    public static void Run(string directory, Action<bool, string> check)
    {
        Directory.CreateDirectory(directory);
        string original = Path.Combine(directory, "original.otocad");
        string logo = Path.Combine(directory, "source-logo.png");
        using (var bitmap = new SKBitmap(4, 4))
        {
            bitmap.Erase(SKColors.Red);
            using var file = File.Create(logo);
            bitmap.Encode(file, SKEncodedImageFormat.Png, 100);
        }
        var raster = new lcdb.Image(logo, new Vector2(20, 30), new Vector2(10, 10));
        var hiddenLine = new Line(new Vector2(50, 20), new Vector2(50, 40));
        var canvas = new CadCanvas();
        canvas.LoadGeneratedSheet(new Entity[] { raster, hiddenLine });
        var hidden = canvas.GetOrCreateLayer("内部备注");
        hidden.IsVisible = false;
        hidden.IsFrozen = true;
        canvas.AssignEntityToLayer(hiddenLine, hidden);
        canvas.SaveCurrentScene(original);
        canvas.MarkDirty();
        string stem = Path.Combine(directory, "copy");
        TemplatePipeline.Export(canvas, stem, new[] { "json", "otocad", "png", "pdf", "dxf" });
        check(canvas.CurrentFilePath == original && canvas.IsDirty, "导出五格式保留图纸路径和编辑标记");

        var dxf = netDxf.DxfDocument.Load(stem + ".dxf");
        var layer = dxf.Layers["内部备注"];
        check(!layer.IsVisible && layer.IsFrozen && dxf.Entities.Lines.Single().Layer == layer,
            "DXF 读回保留隐藏与冻结状态和实体图层");
        var bitmapRef = dxf.Entities.Images.Single().Definition.File;
        check(!Path.IsPathRooted(bitmapRef) && File.Exists(Path.Combine(directory, bitmapRef)),
            "DXF 图像资源使用可搬移的相对路径");

        string moved = Path.Combine(directory, "other");
        Directory.CreateDirectory(moved);
        File.Delete(logo);
        foreach (string format in new[] { "json", "otocad" })
        {
            string relocated = Path.Combine(moved, "alone." + format);
            File.Copy(stem + "." + format, relocated, true);
            var opened = new CadCanvas();
            opened.LoadOtocadFile(relocated);
            var persisted = opened.GetAllEntities().OfType<lcdb.Image>().Single();
            check(persisted.GetImageBytes().Length > 0 && !Path.IsPathRooted(persisted.imagePath),
                format + " 单文件换目录后仍包含原始 Logo");
            var line = opened.GetAllEntities().OfType<Line>().Single();
            check(!opened.IsEntityVisible(line), format + " 隐藏图层在重开后仍生效");
            string png = Path.Combine(moved, format + ".png");
            opened.ExportPaperPng(png, Vector2.Zero, 100, 60);
            using var image = SKBitmap.Decode(png);
            double px = 200 / 25.4;
            var center = image.GetPixel((int)(25 * px), (int)(25 * px));
            check(center.Red > 240 && center.Green < 10, format + " 搬移后可绘出红色 Logo");
            check(Enumerable.Range(-2, 5).All(dx =>
                image.GetPixel((int)(50 * px) + dx, (int)(30 * px)) == SKColors.White),
                format + " 导出的 PNG 不含隐藏线");
        }

        var first = new CadCanvas();
        var second = new CadCanvas();
        first.LoadOtocadFile(original);
        second.LoadOtocadFile(original);
        ((OtoCAD.Avalonia.Commands.ICadCommandHost)first).AddEntity(new Line(new Vector2(6, 7), new Vector2(9, 10)));
        first.SaveCurrentScene(original);
        second.MarkDirty();
        byte[] prior = File.ReadAllBytes(original);
        bool blocked = false;
        try { second.SaveCurrentScene(original); }
        catch (IOException) { blocked = true; }
        check(blocked && second.IsDirty && prior.SequenceEqual(File.ReadAllBytes(original)),
            "两个窗口保存同一文件时能阻止旧版覆盖");
        second.SaveCurrentScene(Path.Combine(directory, "另存副本.otocad"));
        check(!second.IsDirty, "外部变更后可保存独立副本");

        var portrait = new CadCanvas();
        portrait.LoadGeneratedSheet(new Entity[] { new IsoLensDrawingFrame { PaperWidth = 210, PaperHeight = 297 } });
        TemplatePipeline.Export(portrait, Path.Combine(directory, "portrait"), new[] { "pdf" });
    }
}
