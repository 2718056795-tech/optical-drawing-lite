using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using lcdb;
using lcdb.DrawingFrame;
using OtoCAD.Avalonia.Templating;

namespace OtoCAD.Avalonia.Slim;

public static partial class TemplatePipeline
{
    public static bool IsTechnicalNotesField(DrawingFrame frame, PropertyCellHit cell)
    {
        if (string.IsNullOrEmpty(cell.FieldKey)) return false;
        if (frame is DataDrivenFrame custom)
            return custom.Definition.FirstBand(FrameDefinition.KindNotes)?.Fields.FirstOrDefault()?.Key == cell.FieldKey;
        return (frame is GbLensDrawingFrame or IsoLensDrawingFrame && cell.FieldKey == "Notes") ||
               (frame is IsoLensDrawingFrame && cell.FieldKey.StartsWith("NoteLine_", StringComparison.Ordinal));
    }

    public static bool IsNamedFrameField(DrawingFrame frame, PropertyCellHit cell)
    {
        if (string.IsNullOrEmpty(cell.FieldKey)) return false;
        if (frame is DataDrivenFrame custom) return custom.Definition.NamedFields().Any(f => f.Key == cell.FieldKey);
        return IsTechnicalNotesField(frame, cell) || NamedProperties(frame).Contains(cell.FieldKey);
    }

    private static string FieldSlotKey(DrawingFrame frame, PropertyCellHit cell)
        => frame is DataDrivenFrame ? "field:" + cell.FieldKey :
           frame is IsoLensDrawingFrame && IsTechnicalNotesField(frame, cell) ? "p:Notes" : "p:" + cell.FieldKey;

    public static bool IsGeneratedScaleField(DrawingFrame frame, PropertyCellHit cell) =>
        frame.TemplateTextBindings is not null && (frame switch
        {
            IsoLensDrawingFrame => cell.FieldKey == "DrawScale",
            GbLensDrawingFrame => cell.FieldKey == "ScaleText",
            OpticalDrawingFrame => cell.FieldKey == "Scale",
            DataDrivenFrame => cell.FieldKey is "scale" or "DrawScale",
            _ => false
        });

    public static bool CanOverrideFrameField(DrawingFrame frame, PropertyCellHit cell)
    {
        var bindings = frame.TemplateTextBindings;
        if (bindings is null || string.IsNullOrEmpty(cell.FieldKey) || IsGeneratedScaleField(frame, cell)) return false;
        if (bindings.TryGetValue(FieldSlotKey(frame, cell), out var saved)) return saved.OverrideText is null;
        return frame is GbLensDrawingFrame && cell.FieldKey == "Notes" &&
               bindings.Keys.Any(k => k.StartsWith("note:", StringComparison.Ordinal));
    }

    public static void SetFrameFieldText(DrawingFrame frame, PropertyCellHit cell, string text)
    {
        if (frame is not IPropertyZoneFrame zone || !IsNamedFrameField(frame, cell))
            throw new ArgumentException("请选择图框的命名字段。", nameof(cell));
        if (IsGeneratedScaleField(frame, cell)) throw new InvalidOperationException("图纸比例由零件图区的视图比例决定。");

        var bindings = frame.TemplateTextBindings;
        string slot = FieldSlotKey(frame, cell);
        if (frame is GbLensDrawingFrame gb && cell.FieldKey == "Notes" && bindings is not null &&
            bindings.Keys.Any(k => k.StartsWith("note:", StringComparison.Ordinal)))
        {
            // Preserve even removed lines from the reusable template when replacing a legacy notes block.
            var expressions = gb.NoteLines.Select((line, i) => bindings.TryGetValue("note:" + i, out var old)
                ? ReconcileExpression(old, line) : line);
            string original = string.Join("\n", expressions);
            foreach (var key in bindings.Keys.Where(k => k.StartsWith("note:", StringComparison.Ordinal)).ToArray())
                bindings.Remove(key);
            bindings[slot] = new TemplateTextBinding { Expression = original };
        }
        zone.SetCellText(cell, text);
        if (bindings?.TryGetValue(slot, out var binding) == true)
            binding.OverrideText = frame is IsoLensDrawingFrame iso && slot == "p:Notes" ? iso.Notes : text;
    }
}
