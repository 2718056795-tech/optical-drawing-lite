using System;
using System.Collections.Generic;
using lcdb.Optic;

namespace lcdb
{
    /// <summary>Resolves source-parameter identity without serializing entity references.</summary>
    internal static class DrivingDimensionBinding
    {
        internal static Entity? ResolveOwner(Entity dimension, IReadOnlyList<Entity> entities)
        {
            var owner = ResolveDimensionOwner(dimension, entities);
            if (owner is null) return null;
            string key = string.IsNullOrEmpty(dimension.DrivingParameterKey)
                ? InferParameterKey(dimension, entities) : dimension.DrivingParameterKey;
            return MatchesParameter(dimension, owner, key) ? owner : null;
        }

        internal static Entity? ResolveDimensionOwner(Entity dimension, IReadOnlyList<Entity> entities)
        {
            if (entities is null) return null;
            string hostKey;
            if (dimension is LinearDimension linear)
            {
                if (linear.FirstAnchorIndex < 0 || linear.SecondAnchorIndex < 0 ||
                    string.IsNullOrEmpty(linear.FirstAnchorKey) ||
                    !string.Equals(linear.FirstAnchorKey, linear.SecondAnchorKey, StringComparison.Ordinal)) return null;
                hostKey = linear.FirstAnchorKey;
            }
            else if (dimension is RadialDimension radial && radial.OpticalSurfaceIndex >= 0)
                hostKey = radial.OpticalHostKey;
            else return null;

            if (string.IsNullOrEmpty(hostKey)) return null;
            Entity? owner = null;
            foreach (var entity in entities)
            {
                if (entity is not OpticalLens && entity is not CementedLens) continue;
                if (!string.Equals(LinearDimension.AnchorKey(entity, false), hostKey, StringComparison.Ordinal)) continue;
                if (owner is not null) return null; // A copied key must never select an arbitrary lens.
                owner = entity;
            }
            return owner;
        }

        internal static string InferParameterKey(Entity dimension, IReadOnlyList<Entity> entities)
        {
            var owner = ResolveDimensionOwner(dimension, entities);
            if (owner is null) return "";
            if (!string.IsNullOrEmpty(dimension.DrivingParameterKey))
                return MatchesParameter(dimension, owner, dimension.DrivingParameterKey) ? dimension.DrivingParameterKey : "";
            if (!dimension.IsGenerated || dimension.Source != EntitySource.OpticalElement) return "";

            if (dimension is LinearDimension d)
            {
                if (d.SourceMeasurementScale <= 0 || !double.IsFinite(d.SourceMeasurementScale) ||
                    string.IsNullOrEmpty(d.LastAutomaticText) ||
                    string.IsNullOrEmpty(d.AutoMeasurementText) || !d.AutoMeasurementText.Contains("{value}")) return "";
                if (owner is OpticalLens && Pair(d, 1, 2)) return "Thickness";
                if (owner is CementedLens && Pair(d, 1, 3)) return "T1";
                if (owner is CementedLens && Pair(d, 3, 2)) return "T2";
                if (owner is OpticalLens lens && Pair(d, 8, 7))
                    return DiameterParameter(lens.Diameter, lens.MechanicalDiameter, lens.FrontSurface, lens.BackSurface);
                if (owner is CementedLens glue && Pair(d, 9, 8))
                    return DiameterParameter(glue.Diameter, glue.MechanicalDiameter, glue.FrontSurface, glue.ContactSurface, glue.BackSurface);
                return ""; // Edge thickness and doublet total thickness are derived, never guessed.
            }
            if (dimension is RadialDimension r && r.SourceMeasurementScale > 0 &&
                double.IsFinite(r.SourceMeasurementScale) && !string.IsNullOrEmpty(r.LastAutomaticText))
                return RadiusParameter(r, owner);
            return "";
        }

        private static bool Pair(LinearDimension d, int a, int b) =>
            (d.FirstAnchorIndex == a && d.SecondAnchorIndex == b) ||
            (d.FirstAnchorIndex == b && d.SecondAnchorIndex == a);

        private static bool MatchesParameter(Entity dimension, Entity owner, string key)
        {
            if (dimension is LinearDimension d)
            {
                return key switch
                {
                    "Thickness" when owner is OpticalLens && Pair(d, 1, 2) => true,
                    "T1" when owner is CementedLens && Pair(d, 1, 3) => true,
                    "T2" when owner is CementedLens && Pair(d, 3, 2) => true,
                    "Diameter" or "MechanicalDiameter" when owner is OpticalLens && Pair(d, 8, 7) => true,
                    "Diameter" or "MechanicalDiameter" when owner is CementedLens && Pair(d, 9, 8) => true,
                    _ => false
                };
            }
            var r = (RadialDimension)dimension;
            string expectedKey = RadiusParameter(r, owner);
            return expectedKey.Length > 0 && key == expectedKey;
        }

        private static string RadiusParameter(RadialDimension r, Entity owner) => owner is OpticalLens
                ? r.OpticalSurfaceIndex switch { 0 => "R1", 1 => "R2", _ => "" }
                : r.OpticalSurfaceIndex switch { 0 => "R1", 1 => "RContact", 2 => "R3", _ => "" };

        private static string DiameterParameter(double diameter, double? mechanicalDiameter, params OpticalSurface[] surfaces)
        {
            if (mechanicalDiameter.GetValueOrDefault() > 0) return "MechanicalDiameter";
            double drawnDiameter = 0;
            foreach (var surface in surfaces)
            {
                if (surface.SemiAperture > 1e-9) return "MechanicalDiameter";
                drawnDiameter = Math.Max(drawnDiameter, 2 * OpticSurfaceGeometry.DrawnSemiAperture(surface, diameter / 2));
            }
            return Math.Abs(drawnDiameter - diameter) <= 1e-8 * Math.Max(1, Math.Abs(diameter))
                ? "Diameter" : "MechanicalDiameter";
        }
    }
}
