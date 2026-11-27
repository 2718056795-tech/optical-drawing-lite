using lcdb;
using lcdb.Annotation;
using lcdb.DrawingFrame;

namespace OtoCAD.Avalonia;

public partial class CadCanvas
{
    private static bool HasDefaultLettering(Entity entity) => entity switch
    {
        Text t => string.IsNullOrEmpty(t.TextStyle) || t.TextStyle.Equals("Standard", StringComparison.OrdinalIgnoreCase),
        MText or DimensionBase or DrawingFrame or SurfaceRoughnessMark or AsphericDataBlock => true,
        _ => false
    };

    // Include the saved view regions: later geometry edits regenerate dimensions from their region.
    private IEnumerable<Entity> SheetLettering()
    {
        foreach (var entity in GetAllEntities())
        {
            if (HasDefaultLettering(entity)) yield return entity;
            if (entity.DrawingBinding is { } binding)
                foreach (var region in binding.Regions) yield return region;
        }
    }

    internal bool CanAdoptEngineeringSerif() => SheetLettering().Any(e => !e.UseEngineeringSerif);

    internal bool AdoptEngineeringSerif()
    {
        var targets = SheetLettering().Where(e => !e.UseEngineeringSerif).Distinct().ToArray();
        if (targets.Length == 0) return false;
        ApplyEdit(() =>
        {
            foreach (var target in targets)
            {
                target.UseEngineeringSerif = true;
                target.InvalidateRenderCache();
                if (target is DimensionBase dimension) dimension.Update();
            }
        });
        return true;
    }
}
