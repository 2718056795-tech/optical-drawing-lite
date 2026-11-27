using lcdb;
using lcdb.Optic;
using OtoCAD.Avalonia.Slim;

namespace OtoCAD.Avalonia;

public partial class MainWindow
{
    private void BindDimensionTextHeights(CadCanvas source)
    {
        _propertyPanel.CommitDimensionHeight = (dimension, height) =>
        {
            if (ReferenceEquals(source, _activeCanvas) && source.GetAllEntities().Contains(dimension))
                source.ApplyEdit(() => SetHeight(dimension, height));
        };
        _propertyPanel.BatchForDimensionHeight = dimension =>
        {
            var entities = source.GetAllEntities();
            if (!TemplatePipeline.IsGeneratedDrawing(entities) || DimensionTextRole.Find(dimension, entities) is not string role)
                return null;
            return height => ApplyToDrawingTabs(source, canvas =>
            {
                var current = canvas.GetAllEntities();
                if (ReferenceEquals(canvas, source) && !current.Contains(dimension))
                    throw new InvalidOperationException("所选标注已改变，请重新选择。");
                var matches = current.OfType<DimensionBase>().Where(d => DimensionTextRole.Find(d, current) == role).ToArray();
                if (matches.Length == 0) throw new InvalidOperationException("没有对应的尺寸标注。");
                var changed = matches.Where(d => d.TextHeightOverride != height).ToArray();
                if (changed.Length == 0) return false;
                canvas.ApplyEdit(() => { foreach (var d in changed) SetHeight(d, height); });
                return true;
            });
        };
    }

    private static void SetHeight(DimensionBase dimension, double? height)
    {
        dimension.TextHeightOverride = height;
        dimension.Generate();
    }
}

internal static class DimensionTextRole
{
    // A sheet view's host and surface/endpoint bindings identify even translated or renamed dimensions.
    // Manually created, unassociated dimensions only match the same CAD dimension type.
    internal static string? Find(DimensionBase dim, IReadOnlyList<Entity> entities)
    {
        var owner = dim.ResolveDimensionOwner(entities);
        if (owner is not null && dim is RadialDimension radius)
        {
            return (radius.OpticalSurfaceIndex, owner) switch
            {
                (0, _) => "FrontRadius", (1, OpticalLens) or (2, CementedLens) => "BackRadius",
                (1, CementedLens) => "ContactRadius", _ => null
            };
        }
        if (owner is not null && dim is LinearDimension linear)
        {
            int lo = Math.Min(linear.FirstAnchorIndex, linear.SecondAnchorIndex);
            int hi = Math.Max(linear.FirstAnchorIndex, linear.SecondAnchorIndex);
            return (lo, hi, owner) switch
            {
                (1, 2, OpticalLens) => "Thickness", (1, 2, CementedLens) => "TotalThickness",
                (1, 3, CementedLens) => "T1", (2, 3, CementedLens) => "T2",
                (5, 7, OpticalLens) or (6, 8, CementedLens) => "EdgeThickness",
                (7, 8, OpticalLens) or (8, 9, CementedLens) => dim.InferDrivingParameterKey(entities) switch
                { "Diameter" => "Diameter", "MechanicalDiameter" => "MechanicalDiameter", _ => null },
                _ => null
            };
        }
        if (!string.IsNullOrEmpty(dim.OpticalDimensionRole)) return dim.OpticalDimensionRole;
        if (dim.IsGenerated || dim.Source == EntitySource.OpticalElement ||
            dim is LinearDimension { FirstAnchorIndex: >= 0 } or LinearDimension { SecondAnchorIndex: >= 0 } ||
            dim is RadialDimension { OpticalSurfaceIndex: >= 0 } || dim.Owner is OpticalLens or CementedLens)
            return null;
        return "Manual:" + dim.dimensionType;
    }
}
