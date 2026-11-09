using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia;
using lcdb;
using LitMath;

namespace OtoCAD.Avalonia.Slim;

/// <summary>Runs on an initialized Avalonia UI thread with real canvas measure/arrange events.</summary>
public static class ViewportFitValidation
{
    public static IReadOnlyList<string> Run()
    {
        var log = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("画布自适应: " + message);
            log.Add("PASS " + message);
        }
        static void Resize(CadCanvas canvas, double width, double height)
        {
            canvas.Measure(new Size(width, height));
            canvas.Arrange(new Rect(0, 0, width, height));
        }
        static double ScaleFor(double width, double height) => Math.Min((width - 24) / 210, (height - 24) / 297);
        static float Offset(CadCanvas canvas, string name) =>
            (float)typeof(CadCanvas).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;

        var canvas = new CadCanvas();
        canvas.LoadGeneratedSheet(new Entity[] { new Line(Vector2.Zero, new Vector2(210, 297)) });
        canvas.ResetView(); // Repeated pre-layout requests must not accumulate callbacks.
        Resize(canvas, 700, 800);
        Check(Math.Abs(canvas.CurrentScale - ScaleFor(700, 800)) < 1e-5,
            "首次布局按实际可用高度等比适配，并保留每侧12像素边距");
        Resize(canvas, 1050, 950);
        Check(Math.Abs(canvas.CurrentScale - ScaleFor(1050, 950)) < 1e-5,
            "整页适应状态随画布可用高度更新");

        canvas.ZoomBy(1.5f);
        float manualScale = canvas.CurrentScale;
        float manualX = Offset(canvas, "_offsetX"), manualY = Offset(canvas, "_offsetY");
        Resize(canvas, 760, 650);
        Check(canvas.CurrentScale == manualScale && Offset(canvas, "_offsetX") == manualX && Offset(canvas, "_offsetY") == manualY,
            "手动缩放后，窗口变化保留缩放和偏移");

        var other = new CadCanvas();
        other.LoadGeneratedSheet(new Entity[] { new Line(Vector2.Zero, new Vector2(210, 297)) });
        Resize(other, 760, 650);
        other.ResetView();
        Resize(canvas, 700, 600);
        Check(canvas.CurrentScale == manualScale && Offset(canvas, "_offsetX") == manualX && Offset(canvas, "_offsetY") == manualY,
            "其他文档的适应整页操作不改变当前文档的手动视图");

        canvas.ResetView();
        Resize(canvas, 700, 850);
        Check(Math.Abs(canvas.CurrentScale - ScaleFor(700, 850)) < 1e-5,
            "再次点击适应整页后恢复随窗口自动适配");
        canvas.ZoomToOneToOne();
        Resize(canvas, 700, 900);
        Check(canvas.CurrentScale == 1, "选择1:1后保持手工比例，不被自动适配覆盖");
        return log;
    }
}
