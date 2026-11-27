using System.Globalization;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using LitMath;
using OtoCAD.Avalonia.Commands;

namespace OtoCAD.Avalonia.Slim;

public static partial class TemplatePipeline
{
    internal static bool IsSagDimension(DimensionBase dimension) =>
        dimension.OpticalDimensionRole is "front-sag" or "contact-sag" or "back-sag";

    private readonly record struct SagFace(string Role, OpticalSurface Surface, double AxialOffset, int VertexIndex, bool TextOnRight)
    {
        internal bool IsConcaveSphere => Surface is SphericalSurface && double.IsFinite(Surface.Radius) &&
            (Role == "front-sag" ? Surface.Radius < 0 : Role == "back-sag" ? Surface.Radius > 0 : Surface.Radius != 0);
    }

    private static IEnumerable<SagFace> SagFaces(Entity view)
    {
        if (view is OpticalLens single)
        {
            yield return new("front-sag", single.FrontSurface, -single.Thickness / 2, 1, false);
            yield return new("back-sag", single.BackSurface, single.Thickness / 2, 2, true);
        }
        else if (view is CementedLens glue)
        {
            double half = (glue.T1 + glue.T2) / 2;
            yield return new("front-sag", glue.FrontSurface, -half, 1, false);
            yield return new("contact-sag", glue.ContactSurface, glue.T1 - half, 3, glue.ContactSurface.Radius > 0);
            yield return new("back-sag", glue.BackSurface, half, 2, true);
        }
    }

    private static bool TryGetSag(SagFace face, out double signedSag)
    {
        signedSag = 0;
        double h = face.Surface.SemiAperture;
        double r = Math.Abs(face.Surface.Radius);
        if (!face.IsConcaveSphere || !double.IsFinite(h) || h <= 0 || h > r) return false;
        // Rationalized form of R-sqrt(R²-h²) retains precision for shallow curves.
        double ratio = h / r;
        signedSag = Math.CopySign(h * ratio / (1 + Math.Sqrt(1 - ratio * ratio)), face.Surface.Radius);
        return double.IsFinite(signedSag) && signedSag != 0;
    }

    private static Vector2 ViewPosition(Entity view) =>
        view is OpticalLens single ? single.Position : ((CementedLens)view).Position;

    private static TemplateViewRegion? SagRegion(Entity view)
    {
        Vector2 xy = ViewPosition(view);
        return view.DrawingBinding?.Regions.FirstOrDefault(r => xy.X >= r.X && xy.X <= r.X + r.PaperWidth &&
            xy.Y >= r.Y && xy.Y <= r.Y + r.PaperHeight);
    }

    // Build on actual visible geometry. The measured span uses the surface's own
    // clear-aperture radius, even if its continuation extends to a wider flange.
    private static LinearDimension? UpdateSagDimension(Entity view, SagFace face, LinearDimension? dimension,
        TemplateViewRegion? region)
    {
        if (!TryGetSag(face, out var signedSag) || view.DrawingBinding is not { ViewScale: > 0 } binding) return null;
        double value = Math.Abs(signedSag) / binding.ViewScale;
        string prefix = face.Role == "contact-sag" ? "胶合面矢高 " : "矢高 ";
        string formatted = prefix + (value < 0.0005 ? "<0.001" : value.ToString("F3", CultureInfo.InvariantCulture));
        var origin = ViewPosition(view);
        double angle = (view is OpticalLens single ? single.Rotation : ((CementedLens)view).Rotation) * Math.PI / 180;
        Vector2 Point(double x, double y) => origin + Vector2.RotateInRadian(new Vector2(x, y), new Vector2(), angle);
        var vertex = Point(face.AxialOffset, 0);
        var rim = Point(face.AxialOffset + signedSag, face.Surface.SemiAperture);
        if (dimension is null)
        {
            var style = IsoSingleLensSheet.IsoDimStyle(SheetDimensionHeight(region));
            style.DimensionLineGap = 1.3;
            style.ArrowSize = 1.6;
            style.ExtensionLineExtend = 1.2;
            style.ExtensionLineOffset = Math.Max(0, region?.DimensionExtensionGap ?? 0.6);
            dimension = new LinearDimension(vertex, rim, 0, angle, style)
            {
                OpticalDimensionRole = face.Role,
                Owner = view,
                FirstAnchor = new DimAnchor(view, face.VertexIndex),
                Source = EntitySource.OpticalElement,
                IsGenerated = true,
                SourceMeasurementScale = binding.ViewScale,
                UseEngineeringSerif = region?.UseEngineeringSerif == true,
                RotateWithOpticalHost = true,
                LastHostRotation = angle * 180 / Math.PI,
                userText = formatted,
                LastAutomaticText = formatted
            };
            double upper = view is OpticalLens lens
                ? OpticSurfaceGeometry.OuterDiameter(lens.Diameter, lens.MechanicalDiameter, lens.FrontSurface, lens.BackSurface) / 2
                : DoubletOuterRadius((CementedLens)view);
            double tier = face.Role == "contact-sag" ? 24 : 16;
            dimension.SetDimensionLinePosition(Point(face.AxialOffset, upper + tier));
            dimension.ShortSpanTextOnRight = face.TextOnRight;
            dimension.Update();
            return dimension;
        }

        bool renamed = dimension.userText == dimension.LastAutomaticText && formatted != dimension.userText;
        if (renamed) dimension.userText = formatted;
        dimension.LastAutomaticText = formatted;
        if (dimension.firstReferencePoint != vertex || dimension.secondReferencePoint != rim || renamed)
        {
            dimension.firstReferencePoint = vertex;
            dimension.secondReferencePoint = rim;
            dimension.Update();
        }
        return dimension;
    }

    private static double DoubletOuterRadius(CementedLens lens) =>
        OpticSurfaceGeometry.OuterDiameter(lens.Diameter, lens.MechanicalDiameter, lens.FrontSurface, lens.ContactSurface, lens.BackSurface) / 2;

    private static IEnumerable<LinearDimension> CreateSagDimensions(Entity view, TemplateViewRegion? region) =>
        SagFaces(view).Select(face => UpdateSagDimension(view, face, null, region)).OfType<LinearDimension>();

    private static void PositionEdgeThicknessCaption(IReadOnlyList<Entity> entities, Entity owner, bool hasSag)
    {
        foreach (var dimension in entities.OfType<LinearDimension>())
        {
            if (dimension.ShortSpanTextOnRight is null || !dimension.userText.StartsWith("(", StringComparison.Ordinal) ||
                dimension.PlaceSpanTextOutside == hasSag || !ReferenceEquals(dimension.ResolveDimensionOwner(entities), owner))
                continue;
            // All concave-surface witness lines rise past the edge-thickness span. The
            // left landing keeps its caption clear of the witness lines on the section.
            dimension.PlaceSpanTextOutside = hasSag;
            dimension.Update();
        }
    }

    private static void RefreshSagDimensions(IReadOnlyList<Entity> entities, IReadOnlyList<Entity> owners, bool geometryChanged)
    {
        var sag = entities.OfType<LinearDimension>().Where(IsSagDimension).ToLookup(d => d.ResolveDimensionOwner(entities));
        var writable = entities as IList<Entity>;
        if (writable?.IsReadOnly == true) writable = null;
        foreach (var owner in owners)
        {
            bool hasSag = false;
            foreach (var face in SagFaces(owner))
            {
                var existing = sag[owner].FirstOrDefault(d => d.OpticalDimensionRole == face.Role);
                if (existing is null && (!geometryChanged || writable is null)) continue;
                var updated = UpdateSagDimension(owner, face, existing, existing is null ? SagRegion(owner) : null);
                hasSag |= updated is not null;
                if (existing is null && updated is not null) writable!.Add(updated);
                if (existing is not null && updated is null) writable?.Remove(existing);
            }
            PositionEdgeThicknessCaption(entities, owner, hasSag);
        }
    }
}
