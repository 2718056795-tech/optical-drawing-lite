using lcdb;
using lcdb.DrawingFrame;

namespace OtoCAD.Avalonia;

internal readonly record struct TableFormat(double RowPitch, double BodyHeight, double HeaderHeight)
{
    internal static TableFormat? Read(DrawingFrame frame)
    {
        TableFormat? defaults = frame switch
        {
            IsoLensDrawingFrame iso => new(iso.TableRowHeight, iso.CellTextHeight, iso.HeaderTextHeight()),
            GbLensDrawingFrame gb => new(gb.TableRowHeight, gb.CellTextHeight, 2.6),
            OpticalDrawingFrame optical => new(optical.TableRowHeight, 2.2, 2.5),
            TemplateTable free => new(free.RowHeight, free.TextHeight, free.TextHeight),
            DataDrivenFrame custom when custom.Definition.FirstBand(FrameDefinition.KindSpecTable) is { } band =>
                new(band.RowHeight > 0 ? band.RowHeight : 6,
                    custom.Definition.Style.CellTextHeight, custom.Definition.Style.HeaderTextHeight),
            _ => null
        };
        return defaults is { } value ? new(value.RowPitch, frame.SpecificationBodyHeight ?? value.BodyHeight,
            frame.SpecificationHeaderHeight ?? value.HeaderHeight) : null;
    }

    internal string? Error()
    {
        if (!double.IsFinite(RowPitch) || !double.IsFinite(BodyHeight) || !double.IsFinite(HeaderHeight)
            || RowPitch <= 0 || BodyHeight <= 0 || HeaderHeight <= 0)
            return "请输入大于零的毫米数。";
        if (RowPitch < Math.Max(BodyHeight, HeaderHeight))
            return "行间距小于字高，请增大行间距或减小字高。";
        return null;
    }

    internal void Apply(DrawingFrame frame)
    {
        var original = Read(frame) ?? throw new InvalidOperationException("图纸没有对应的参数表。");
        if (RowPitch != original.RowPitch)
        {
            switch (frame)
            {
                case IsoLensDrawingFrame iso: iso.TableRowHeight = RowPitch; break;
                case GbLensDrawingFrame gb: gb.TableRowHeight = RowPitch; break;
                case OpticalDrawingFrame optical: optical.TableRowHeight = RowPitch; break;
                case TemplateTable free: free.RowHeight = RowPitch; break;
                case DataDrivenFrame custom:
                    var definition = custom.Definition.Clone();
                    foreach (var band in definition.Bands.Where(b => b.Kind == FrameDefinition.KindSpecTable))
                        band.RowHeight = RowPitch;
                    custom.Definition = definition;
                    break;
            }
        }
        if (BodyHeight != original.BodyHeight) frame.SpecificationBodyHeight = BodyHeight;
        if (HeaderHeight != original.HeaderHeight) frame.SpecificationHeaderHeight = HeaderHeight;
        frame.InvalidateRenderCache();
    }
}

internal readonly record struct TableFormatChange(double? Pitch, double? Body, double? Header)
{
    internal static TableFormatChange Between(TableFormat old, TableFormat next) =>
        new(old.RowPitch != next.RowPitch ? next.RowPitch : null,
            old.BodyHeight != next.BodyHeight ? next.BodyHeight : null,
            old.HeaderHeight != next.HeaderHeight ? next.HeaderHeight : null);

    internal TableFormat Merge(TableFormat current) =>
        new(Pitch ?? current.RowPitch, Body ?? current.BodyHeight, Header ?? current.HeaderHeight);
}

public partial class MainWindow
{
    private static DrawingFrame MatchingTable(DrawingFrame source, IReadOnlyList<Entity> entities)
    {
        var tables = entities.OfType<DrawingFrame>().Where(f => TableFormat.Read(f).HasValue &&
            (f is TemplateTable) == (source is TemplateTable)).ToList();
        if (tables.Contains(source)) return source;
        if (source is TemplateTable free)
            tables = tables.OfType<TemplateTable>().Where(f =>
                f.SourceFrameLayoutKey is { } key && key == free.SourceFrameLayoutKey ||
                f.Columns.Select(c => c.Title).SequenceEqual(free.Columns.Select(c => c.Title))).Cast<DrawingFrame>().ToList();
        if (tables.Count > 1 && source.SourceFrameLayoutKey is { } signature &&
            tables.Any(f => f.SourceFrameLayoutKey == signature))
            tables = tables.Where(f => f.SourceFrameLayoutKey == signature).ToList();
        if (tables.Count > 1) tables = tables.Where(f => f.Origin == source.Origin).ToList();
        return tables.Count == 1 ? tables[0] : throw new InvalidOperationException("没有唯一对应的参数表。");
    }

    private BatchPropertyResult ApplyTableFormatToAll(CadCanvas sourceCanvas, DrawingFrame source, TableFormatChange change) =>
        ApplyToDrawingTabs(sourceCanvas, canvas =>
        {
            var frame = MatchingTable(source, canvas.GetAllEntities());
            var original = TableFormat.Read(frame)!.Value;
            var format = change.Merge(original);
            if (format == original) return false;
            if (format.Error() is { } error) throw new InvalidOperationException(error);
            canvas.ApplyEdit(() => format.Apply(frame));
            return true;
        });
}
