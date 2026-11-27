using lcdb;
using lcdb.Annotation;
using lcdb.DrawingFrame;

namespace OtoCAD.Avalonia.Slim;

public static partial class TemplatePipeline
{
    private static void SetViewLettering(List<Entity> entities, Entity owner, TemplateViewRegion region)
    {
        if (region.ViewLabelHeight is not double height) return;
        foreach (var entity in entities.Where(e => ReferenceEquals(e.Owner, owner)))
        {
            if (entity is SurfaceRoughnessMark finish)
                finish.RoughnessTextHeight = height + 0.5;
            else if (entity is MText { Value: "opt. axis" } label)
                label.Height = height;
        }
    }

    private static double ClearSurfaceMarkText(IReadOnlyList<Entity> entities, Entity owner, double minX)
    {
        foreach (var mark in entities.OfType<SurfaceRoughnessMark>())
            if (ReferenceEquals(mark.Owner, owner) && mark.ShowText && mark.RoughnessTextHeight is double height)
                minX = Math.Max(minX, mark.bounding.right + height + 1.5);
        return minX;
    }
}
