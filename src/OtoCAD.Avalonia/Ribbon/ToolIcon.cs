using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace OtoCAD.Avalonia.Ribbon;

/// <summary>Monochrome tool symbols drawn in a 24-unit grid.</summary>
public sealed class ToolIcon : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<ToolIcon>();

    private static readonly Dictionary<string, StreamGeometry> Shapes = new(StringComparer.Ordinal)
    {
        ["import"] = Shape("M4 14v6h16v-6 M12 3v12 m-4-4 4 4 4-4"),
        ["export"] = Shape("M4 14v6h16v-6 M12 15V3 m-4 4 4-4 4 4"),
        ["open"] = Shape("M3 18V5h7l2 3h8v3 M3 18l4-7h15l-4 7z"),
        ["save"] = Shape("M4 4h13l3 3v13H4z M8 4v6h8V4 M8 20v-6h8v6"),
        ["save-as"] = Shape("M12 20H4V4h13l3 3v3 M8 4v6h8V4 M8 20v-6h3 M14 19l-1 3 3-1 6-6-2-2z"),
        ["library"] = Shape("M4 4h4v16H4z M10 4h4v16h-4z M16 5l3-1 4 15-3 1z M4 8h4 M10 15h4"),
        ["template-new"] = Shape("M12 20H5V4h10l4 4v4 M15 4v4h4 M18 13v8 M14 17h8"),
        ["template-save"] = Shape("M10 20H4V4h10l4 4v3 M14 4v4h4 M7 9h4 M7 13h3 M12 17l3 3 6-6"),
        ["star"] = Shape("M12 3l2.7 5.6 6.2.9-4.5 4.4 1.1 6.1-5.5-2.9-5.5 2.9 1.1-6.1-4.5-4.4 6.2-.9z"),
        ["template-use"] = Shape("M11 20H5V4h10l4 4v3 M15 4v4h4 M13 17h9 m-4-4 4 4-4 4"),
        ["layout"] = Shape("M3 5h18v14H3z M9 5v14 M12 9h6 M12 13h6 M12 16h4"),
        ["region"] = Shape("M4 9V4h5 M15 4h5v5 M20 15v5h-5 M9 20H4v-5 M8 8l8 8 M16 8l-8 8"),
        ["table"] = Shape("M3 5h18v14H3z M3 10h18 M3 14h18 M9 5v14 M15 5v14"),
        ["table-edit"] = Shape("M10 19H3V5h18v5 M3 10h10 M3 14h7 M8 5v14 M13 18l-1 4 4-1 6-6-3-3z"),
        ["parameters"] = Shape("M8 4H5v16h3 M16 4h3v16h-3 M9 15l3-7 3 7 M10 13h4"),
        ["line"] = Shape("M5 18L19 6 M3 16h4v4H3z M17 4h4v4h-4z"),
        ["rectangle"] = Shape("M3 6h18v12H3z"),
        ["circle"] = Shape("M21 12a9 9 0 1 1-18 0 9 9 0 1 1 18 0z"),
        ["arc"] = Shape("M4 17a8 8 0 0 1 16-9 M3 15h3v3H3z M18 7h3v3h-3z"),
        ["text"] = Shape("M5 7V4h14v3 M12 4v16 M8 20h8"),
        ["paragraph"] = Shape("M4 5h16 M4 10h11 M4 15h16 M4 20h9"),
        ["image"] = Shape("M3 4h18v16H3z M3 17l5-5 4 4 3-3 6 6 M17 9a1.6 1.6 0 1 1-3.2 0 1.6 1.6 0 1 1 3.2 0z"),
        ["move"] = Shape("M12 3v18 M3 12h18 M9 6l3-3 3 3 M9 18l3 3 3-3 M6 9l-3 3 3 3 M18 9l3 3-3 3"),
        ["copy"] = Shape("M8 16H3V3h13v5 M8 8h13v13H8z"),
        ["delete"] = Shape("M4 6h16 M9 6V3h6v3 M6 6l1 15h10l1-15 M10 10v7 M14 10v7"),
        ["undo"] = Shape("M9 4 4 9l5 5 M4 9h11a5 5 0 0 1 0 10h-4"),
        ["redo"] = Shape("M15 4l5 5-5 5 M20 9H9a5 5 0 0 0 0 10h4"),
        ["fit"] = Shape("M3 9V3h6 M15 3h6v6 M21 15v6h-6 M9 21H3v-6 M8 8h8v8H8z"),
        ["zoom-in"] = Shape("M16 10a6 6 0 1 1-12 0 6 6 0 1 1 12 0z M15 15l6 6 M7 10h6 M10 7v6"),
        ["zoom-out"] = Shape("M16 10a6 6 0 1 1-12 0 6 6 0 1 1 12 0z M15 15l6 6 M7 10h6"),
        ["grid"] = Shape("M4 4h16v16H4z M4 9h16 M4 15h16 M9 4v16 M15 4v16"),
        ["snap"] = Shape("M5 5h4v7a3 3 0 0 0 6 0V5h4v7a7 7 0 0 1-14 0z M5 8h4 M15 8h4"),
        ["iso"] = Shape("M5 3h10l4 4v14H5z M15 3v4h4 M8 10h8 M8 10v7h8v-7 M11 10v7 M13 10v7"),
        ["gb"] = Shape("M5 3h10l4 4v14H5z M15 3v4h4 M8 13h8 M8 11v5h8v-5z M12 10v8"),
        ["blank"] = Shape("M5 3h10l4 4v14H5z M15 3v4h4"),
        ["gb-example"] = Shape("M5 3h10l4 4v14H5z M15 3v4h4 M9 17v-6l3 4 3-4v6"),
        ["part-name"] = Shape("M3 8l5-4h13v15H8l-5-4z M7 11v1 M11 9h6 M11 14h6"),
        ["part-number"] = Shape("M8 4 6 20 M16 4l-2 16 M4 9h16 M3 15h16"),
        ["material"] = Shape("M12 3l9 5-9 5-9-5z M3 12l9 5 9-5 M3 16l9 5 9-5"),
        ["material-1"] = Shape("M9 4l6 4-6 4-6-4z M3 12l6 4 6-4 M18 14l2-2v8 M18 20h4"),
        ["material-2"] = Shape("M9 4l6 4-6 4-6-4z M3 12l6 4 6-4 M17 13q3-3 5 0 1 2-2 4l-3 3h5"),
        ["radius-front"] = Shape("M10 3a10 10 0 0 0 0 18 M5 12h15 m-3-3 3 3-3 3"),
        ["radius-back"] = Shape("M14 3a10 10 0 0 1 0 18 M19 12H4 m3-3-3 3 3 3"),
        ["radius-contact"] = Shape("M5 4q7 8 0 16 M12 4q7 8 0 16 M19 4q7 8 0 16 M8 12h8"),
        ["thickness"] = Shape("M4 5v14 M20 5v14 M4 12h16 M8 9l-4 3 4 3 M16 9l4 3-4 3"),
        ["thickness-1"] = Shape("M3 4v16 M13 4v16 M3 12h10 M6 9l-3 3 3 3 M10 9l3 3-3 3 M17 10l2-2v9 M17 17h4"),
        ["thickness-2"] = Shape("M3 4v16 M13 4v16 M3 12h10 M6 9l-3 3 3 3 M10 9l3 3-3 3 M16 10q3-4 5-1 2 2-1 4l-4 4h6"),
        ["aperture"] = Shape("M19 12a7 7 0 1 1-14 0 7 7 0 1 1 14 0z M5 20 19 4"),
        ["aperture-front"] = Shape("M12 4a8 8 0 0 0 0 16 M5 12h16 m-3-3 3 3-3 3"),
        ["aperture-back"] = Shape("M12 4a8 8 0 0 1 0 16 M19 12H3 m3-3-3 3 3 3"),
        ["index"] = Shape("M5 17V8 M5 12q0-4 4-4t4 4v5 M16 6h4 M18 4v4"),
        ["abbe"] = Shape("M4 8l5 9 5-9 M17 14h4 M17 17h4"),
        ["scale"] = Shape("M5 19 19 5 M13 5h6v6 M5 13v6h6 M4 4h5 M4 4v5 M20 20h-5 M20 20v-5"),
        ["date"] = Shape("M4 6h16v14H4z M4 10h16 M8 3v5 M16 3v5 M8 14h2 M13 14h2 M8 17h2"),
        ["source"] = Shape("M5 3h10l4 4v14H5z M15 3v4h4 M10 11l-3 3 3 3 M14 11l3 3-3 3"),
        ["index-1"] = Shape("M4 17V8 M4 12q0-4 4-4t4 4v5 M16 10l2-2v9 M16 17h4"),
        ["index-2"] = Shape("M4 17V8 M4 12q0-4 4-4t4 4v5 M15 10q3-4 5-1 2 2-1 4l-4 4h6"),
        ["abbe-1"] = Shape("M4 8l5 9 5-9 M17 10l2-2v9 M17 17h4"),
        ["abbe-2"] = Shape("M3 8l5 9 5-9 M16 10q3-4 5-1 2 2-1 4l-4 4h6"),
        ["radius-contact-1"] = Shape("M4 4q9 8 0 16 M7 12h7 M17 9l2-2v10 M17 17h4"),
        ["radius-contact-2"] = Shape("M5 4q9 8 0 16 M7 12h7 M16 9q3-4 5-1 2 2-1 4l-4 5h6"),
        ["aperture-contact"] = Shape("M18 12a6 6 0 1 1-12 0 6 6 0 1 1 12 0z M12 3v18 M3 12h18"),
        ["select-fit"] = Shape("M3 9V3h6 M15 3h6v6 M21 15v6h-6 M9 21H3v-6 M12 8v8 M8 12h8"),
        ["clear-sheets"] = Shape("M3 5h12 M6 2v3 M3 8v11h9 M7 8v7 M16 10h5v10h-8v-7z M16 17l2-2 m0 2-2-2"),
        ["chevron"] = Shape("M5 9l7 7 7-7"),
    };

    static ToolIcon() => AffectsRender<ToolIcon>(ForegroundProperty);

    private readonly StreamGeometry _shape;

    public ToolIcon(string glyph, double size = 18)
    {
        Glyph = glyph;
        _shape = Shapes[glyph];
        Width = Height = size;
        IsHitTestVisible = false;
        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
    }

    public string Glyph { get; }

    public static bool IsDefined(string? glyph) => glyph is not null && Shapes.ContainsKey(glyph);

    public override void Render(DrawingContext context)
    {
        double scale = Math.Min(Bounds.Width, Bounds.Height) / 24;
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale));
        var foreground = GetValue(ForegroundProperty) ?? Brushes.Black;
        var pen = new Pen(foreground, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, pen, _shape);
    }

    private static StreamGeometry Shape(string data) => StreamGeometry.Parse(data);
}

