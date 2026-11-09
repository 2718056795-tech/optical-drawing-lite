using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using lcdb;
using lcdb.Optic;

namespace lcdb.Tests;

[TestClass]
public class V16DrivingDimensionBindingTests
{
    private static LinearDimension Linear(Entity owner, int first, int second, string key, string text = "2.00")
    {
        var points = owner.GetSnapPoints();
        var dim = new LinearDimension
        {
            firstReferencePoint = points[first].position, secondReferencePoint = points[second].position,
            FirstAnchor = new(owner, first), SecondAnchor = new(owner, second), Owner = owner,
            DrivingParameterKey = key, userText = text, Source = EntitySource.OpticalElement, IsGenerated = true
        };
        dim.ConfigureAutomaticMeasurement(10);
        return dim;
    }

    private static RadialDimension Radius(Entity owner, int surfaceIndex, string key)
    {
        var dim = new RadialDimension
        {
            Owner = owner, DrivingParameterKey = key, userText = "R50",
            Source = EntitySource.OpticalElement, IsGenerated = true
        };
        dim.ConfigureOpticalReference(owner, surfaceIndex, 10, .5);
        return dim;
    }

    [TestMethod]
    public void Native_roundtrip_keeps_direct_keys_and_the_correct_single_or_cemented_owner()
    {
        foreach (string extension in new[] { "json", "otocad" })
        {
            var single = new OpticalLens { Diameter = 100, Thickness = 20, R1 = 500, R2 = -600 };
            var glue = new CementedLens { Diameter = 100, MechanicalDiameter = 110, T1 = 40, T2 = 20, R1 = 500, RContact = -600, R3 = -700 };
            var entities = new List<Entity>
            {
                single, glue,
                Linear(single, 1, 2, "Thickness"), Linear(single, 8, 7, "Diameter"),
                Radius(single, 0, "R1"), Radius(single, 1, "R2"),
                Linear(glue, 1, 3, "T1", "T1 4.00"), Linear(glue, 3, 2, "T2", "T2 2.00"),
                Linear(glue, 9, 8, "MechanicalDiameter", "Ø11.00"),
                Radius(glue, 0, "R1"), Radius(glue, 1, "RContact"), Radius(glue, 2, "R3"),
                Linear(glue, 1, 2, "", "CT 6.00"), Linear(single, 5, 7, "", "(2.13)")
            };
            var db = new Database();
            foreach (var entity in entities) db.AddEntity(entity);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "." + extension);
            try
            {
                db.SaveAs(path);
                var reopened = new Database(); reopened.Open(path);
                var restored = reopened.GetEntities().ToList();
                var loadedSingle = restored.OfType<OpticalLens>().Single();
                var loadedGlue = restored.OfType<CementedLens>().Single();
                var dimensions = restored.OfType<DimensionBase>().ToList();
                Assert.AreEqual(10, dimensions.Count(d => d.DrivingParameterKey.Length > 0));
                foreach (var dimension in dimensions)
                {
                    var owner = dimension.ResolveDimensionOwner(restored);
                    Assert.IsNotNull(owner);
                    if (dimension.DrivingParameterKey.Length == 0)
                    {
                        Assert.IsNull(dimension.ResolveDrivingOwner(restored));
                        Assert.AreEqual("", dimension.InferDrivingParameterKey(restored));
                        continue;
                    }
                    Assert.AreSame(owner, dimension.ResolveDrivingOwner(restored));
                    if (dimension.DrivingParameterKey is "Thickness" or "Diameter" or "R2") Assert.AreSame(loadedSingle, owner);
                    if (dimension.DrivingParameterKey is "T1" or "T2" or "MechanicalDiameter" or "RContact" or "R3") Assert.AreSame(loadedGlue, owner);
                }
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }

    [TestMethod]
    public void Clone_keeps_key_and_resolves_scene_host_while_duplicate_identity_is_rejected()
    {
        var owner = new OpticalLens { Diameter = 100, Thickness = 20, R1 = 500, R2 = -600 };
        var source = Linear(owner, 1, 2, "Thickness");
        var copiedOwner = (OpticalLens)owner.Clone();
        var copiedDimension = (LinearDimension)source.Clone();
        Assert.AreEqual("Thickness", copiedDimension.DrivingParameterKey);
        Assert.AreSame(copiedOwner, copiedDimension.ResolveDrivingOwner(new Entity[] { copiedOwner, copiedDimension }));
        Assert.IsNull(copiedDimension.ResolveDrivingOwner(new Entity[] { owner, copiedOwner, copiedDimension }));
        var copiedRadius = (RadialDimension)Radius(owner, 1, "R2").Clone();
        Assert.AreEqual("R2", copiedRadius.DrivingParameterKey);
        Assert.AreSame(copiedOwner, copiedRadius.ResolveDrivingOwner(new Entity[] { copiedOwner, copiedRadius }));
    }

    [TestMethod]
    public void Detached_or_retargeted_dimension_cannot_drive_the_original_parameter()
    {
        var owner = new OpticalLens { Diameter = 100, Thickness = 20, R1 = 500, R2 = -600 };
        var another = new OpticalLens { Diameter = 100, Thickness = 30, R1 = 500, R2 = -600 };
        var dim = Linear(owner, 1, 2, "Thickness");
        var scene = new Entity[] { owner, another, dim };
        dim.SecondAnchor = new(another, 2);
        Assert.IsNull(dim.ResolveDrivingOwner(scene));
        dim.SecondAnchor = new(owner, 2); dim.FirstAnchor = null;
        Assert.IsNull(dim.ResolveDrivingOwner(scene));
        dim.FirstAnchor = new(owner, 5); dim.SecondAnchor = new(owner, 7);
        Assert.AreSame(owner, dim.ResolveDimensionOwner(scene));
        Assert.IsNull(dim.ResolveDrivingOwner(scene));
        var radius = Radius(owner, 0, "R2");
        Assert.IsNull(radius.ResolveDrivingOwner(scene));
    }

    [TestMethod]
    public void Legacy_inference_is_limited_to_generated_direct_dimensions_and_preserves_clear_apertures()
    {
        var single = new OpticalLens { Diameter = 100, Thickness = 20, R1 = 500, R2 = -600 };
        var glue = new CementedLens { Diameter = 100, T1 = 40, T2 = 20, R1 = 500, RContact = -600, R3 = -700 };
        var ct = Linear(single, 1, 2, "");
        var diameter = Linear(single, 8, 7, "", "Ø10.00");
        var t1 = Linear(glue, 1, 3, "", "T1 4.00");
        var t2 = Linear(glue, 3, 2, "", "T2 2.00");
        var total = Linear(glue, 1, 2, "", "CT 6.00");
        var edge = Linear(glue, 6, 8, "", "(6.20)");
        var radius = Radius(glue, 1, "");
        var scene = new Entity[] { single, glue, ct, diameter, t1, t2, total, edge, radius };
        Assert.AreEqual("Thickness", ct.InferDrivingParameterKey(scene));
        Assert.AreEqual("Diameter", diameter.InferDrivingParameterKey(scene));
        single.FrontSurface.SemiAperture = 45;
        Assert.AreEqual("MechanicalDiameter", diameter.InferDrivingParameterKey(scene));
        Assert.AreEqual(45, single.FrontSurface.SemiAperture, 1e-9);
        Assert.AreEqual("T1", t1.InferDrivingParameterKey(scene));
        Assert.AreEqual("T2", t2.InferDrivingParameterKey(scene));
        Assert.AreEqual("RContact", radius.InferDrivingParameterKey(scene));
        Assert.AreEqual("", total.InferDrivingParameterKey(scene));
        Assert.AreEqual("", edge.InferDrivingParameterKey(scene));
        Assert.AreSame(glue, total.ResolveDimensionOwner(scene));
        Assert.IsNull(total.ResolveDrivingOwner(scene));
        Assert.AreEqual("", ct.DrivingParameterKey); // Inference does not silently mutate saved metadata.
        ct.IsGenerated = false;
        Assert.AreEqual("", ct.InferDrivingParameterKey(scene));
        ct.IsGenerated = true; ct.AutoMeasurementText = "客户任意文字";
        Assert.AreEqual("", ct.InferDrivingParameterKey(scene));
    }
}
