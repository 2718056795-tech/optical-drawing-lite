using lcdb;
using lcdb.Optic;
using OtoCAD.Avalonia.Commands;

namespace OtoCAD.Avalonia.Slim;

public static partial class TemplatePipeline
{
    private static double SheetDimensionHeight(lcdb.DrawingFrame.TemplateViewRegion? region) => region?.DimensionTextHeight ?? lcdb.DrawingFrame.TemplateViewRegion.LegacyDimensionTextHeight;

    // Existing sheet annotations keep their identity, text, placement and overrides. When explicitly
    // asked to regenerate, reconstruct missing dimensions using the same template and paper scale.
    internal static IEnumerable<DimensionBase> MissingSheetDimensions(IReadOnlyList<Entity> sheet, Entity view)
    {
        var template = RestoreReusableTemplate(sheet);
        var part = SourcePartFromView(view);
        var xy = view is OpticalLens l ? l.Position : ((CementedLens)view).Position;
        var binding = view.DrawingBinding!;
        bool front = !TableIncludesParameter(template, "前表面半径");
        bool back = !TableIncludesParameter(template, "后表面半径");
        bool contact = !TableIncludesParameter(template, "胶合面半径", "胶合面片1半径", "胶合面片2半径");
        var region = binding.Regions?.FirstOrDefault(r => xy.X >= r.X && xy.X <= r.X + r.PaperWidth &&
            xy.Y >= r.Y && xy.Y <= r.Y + r.PaperHeight);
        var height = region is not null ? SheetDimensionHeight(region) : sheet.OfType<DimensionBase>()
            .FirstOrDefault(d => ReferenceEquals(d.ResolveDimensionOwner(sheet), view))?.style.TextHeight ?? SheetDimensionHeight(null);
        var expected = new List<Entity>();
        Entity temp = part is OpticalLens single
            ? IsoSingleLensSheet.AppendScaledLensView(expected, single, xy.X, xy.Y, binding.ViewScale, front, back, height)
            : IsoSingleLensSheet.AppendScaledDoubletView(expected, (CementedLens)part, xy.X, xy.Y, binding.ViewScale, front, contact, back, height);
        var existing = sheet.OfType<DimensionBase>()
            .Where(d => ReferenceEquals(d.ResolveDimensionOwner(sheet), view))
            .Select(d => DimensionTextRole.Find(d, sheet)).ToHashSet();
        var gap = region?.DimensionExtensionGap ?? 0.6;
        if (region is not null) SetViewLettering(expected, temp, region);
        PlaceDimensionsOutside(expected, temp, gap);
        var sagDimensions = CreateSagDimensions(view, region).ToArray();
        PositionEdgeThicknessCaption(expected, temp, sagDimensions.Length > 0);
        foreach (var sag in sagDimensions)
            if (existing.Add(sag.OpticalDimensionRole)) yield return sag;
        foreach (var d in expected.OfType<DimensionBase>())
        {
            d.IsGenerated = true;
            d.Source = EntitySource.OpticalElement;
            var role = DimensionTextRole.Find(d, expected);
            if (role is null || !existing.Add(role)) continue;
            if (d is LinearDimension linear)
            {
                linear.ConfigureAutomaticMeasurement(binding.ViewScale);
                linear.FirstAnchor = new DimAnchor(view, linear.FirstAnchorIndex);
                linear.SecondAnchor = new DimAnchor(view, linear.SecondAnchorIndex);
            }
            if (d is RadialDimension radial)
                radial.ConfigureOpticalReference(view, radial.OpticalSurfaceIndex, binding.ViewScale, radial.OpticalAnchorFraction);
            d.Owner = view;
            yield return d;
        }
    }
}
