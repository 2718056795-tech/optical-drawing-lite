using System;
using System.IO;
using System.Linq;
using lcdb.IO;
using lcdb.Optic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace lcdb.Tests;

[TestClass]
public class CurveExtensionGeometryTests
{
    private static double ArcHalfHeight(Arc arc) =>
        Math.Max(Math.Abs(arc.radius * Math.Sin(arc.startAngle)), Math.Abs(arc.radius * Math.Sin(arc.endAngle)));

    [TestMethod]
    public void OnlyTheRegionBetweenCurveEdgeAndMechanicalRimIsFlat()
    {
        var lens = new OpticalLens
        {
            Diameter = 14.6, MechanicalDiameter = 19.6, Thickness = 5,
            FrontSurface = new SphericalSurface { Radius = 25, SemiAperture = 7.3, CurveSemiAperture = 8.1 },
            BackSurface = new SphericalSurface { Radius = -32, SemiAperture = 6.5, CurveSemiAperture = 9.8 }
        };
        var edges = lens.GetPierceableSubEntities().ToArray();
        var arcs = edges.OfType<Arc>().ToArray();
        Assert.AreEqual(8.1, ArcHalfHeight(arcs.Single(a => a.radius == 25)), 1e-10);
        Assert.AreEqual(9.8, ArcHalfHeight(arcs.Single(a => a.radius == 32)), 1e-10);
        var lands = edges.OfType<Line>().Where(l => Math.Abs(l.startPoint.X - l.endPoint.X) < 1e-10).ToArray();
        Assert.HasCount(2, lands);
        foreach (var land in lands)
        {
            Assert.AreEqual(8.1, Math.Abs(land.startPoint.Y), 1e-10);
            Assert.AreEqual(9.8, Math.Abs(land.endPoint.Y), 1e-10);
            Assert.AreEqual(-2.5 + lens.FrontSurface.Sag(8.1), land.startPoint.X, 1e-10);
        }
        Assert.AreEqual(lands[0].endPoint.X, lens.GetSnapPoints()[5].position.X, 1e-10);
        Assert.AreEqual(7.3, lens.FrontSurface.SemiAperture, 1e-10);
    }

    [TestMethod]
    public void CementedContactAndUnspecifiedMechanicalDiameterSurviveCloneAndFile()
    {
        var lens = new CementedLens
        {
            Diameter = 8, T1 = 4, T2 = 3,
            FrontSurface = new SphericalSurface { Radius = 22, SemiAperture = 4, CurveSemiAperture = 4.3 },
            ContactSurface = new SphericalSurface { Radius = -30, SemiAperture = 3.5, CurveSemiAperture = 4.8 },
            BackSurface = new AsphericSurface { Radius = 42, ConicConstant = -1, SemiAperture = 4, CurveSemiAperture = 5.25 }
        };
        var db = new Database();
        db.AddEntity((CementedLens)lens.Clone());
        string path = Path.Combine(Path.GetTempPath(), $"curve-extension-{Guid.NewGuid():N}.otocad");
        try
        {
            OtocadFileFormatV4.Save(db, path);
            var (opened, _) = OtocadFileFormatV4.Load(path);
            var fresh = opened.GetEntitiesByType<CementedLens>().Single();
            Assert.IsNull(fresh.MechanicalDiameter);
            Assert.AreEqual(4.3, fresh.FrontSurface.CurveSemiAperture);
            Assert.AreEqual(3.5, fresh.ContactSurface.SemiAperture);
            Assert.AreEqual(4.8, fresh.ContactSurface.CurveSemiAperture);
            Assert.AreEqual(5.25, fresh.BackSurface.CurveSemiAperture);
            var edges = fresh.GetPierceableSubEntities().ToArray();
            Assert.AreEqual(4.8, ArcHalfHeight(edges.OfType<Arc>().Single(a => a.radius == 30)), 1e-10);
            Assert.AreEqual(5.25, fresh.GetSnapPoints()[6].position.Y, 1e-10);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void DxfPreservesTheArcEndAndLandOnBothSidesOfTheGlass()
    {
        var db = new Database();
        var lens = new OpticalLens
        {
            Diameter = 14.6, MechanicalDiameter = 19.6, Thickness = 5, ShowOpticalAxis = false, Pattern = GlassPattern.None,
            FrontSurface = new SphericalSurface { Radius = 25, SemiAperture = 7.3, CurveSemiAperture = 8.1 },
            BackSurface = new SphericalSurface { Radius = -32, SemiAperture = 6.5, CurveSemiAperture = 9.8 }
        };
        db.AddEntity(lens);
        string path = Path.Combine(Path.GetTempPath(), $"curve-extension-{Guid.NewGuid():N}.dxf");
        try
        {
            db.SaveAs(path);
            var drawing = netDxf.DxfDocument.Load(path);
            var front = drawing.Entities.Arcs.Single(a => a.Radius == 25);
            double endHeight = Math.Abs(front.Center.Y + front.Radius * Math.Sin(front.EndAngle * Math.PI / 180));
            Assert.AreEqual(8.1, endHeight, 1e-8);
            var lands = drawing.Entities.Lines.Where(l => Math.Abs(l.StartPoint.X - l.EndPoint.X) < 1e-9).ToArray();
            Assert.HasCount(2, lands);
            foreach (var land in lands)
            {
                Assert.AreEqual(-2.5 + lens.FrontSurface.Sag(8.1), land.StartPoint.X, 1e-9);
                Assert.AreEqual(8.1, Math.Abs(land.StartPoint.Y), 1e-9);
                Assert.AreEqual(9.8, Math.Abs(land.EndPoint.Y), 1e-9);
            }
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void UndeclaredExtensionKeepsLegacyClearEdge()
    {
        var surface = new SphericalSurface { Radius = 25, SemiAperture = 5 };
        Assert.IsNull(surface.CurveSemiAperture);
        Assert.AreEqual(5, OpticSurfaceGeometry.DrawnSemiAperture(surface, 7), 1e-10);
        surface.SemiAperture = 0;
        Assert.AreEqual(7, OpticSurfaceGeometry.DrawnSemiAperture(surface, 7), 1e-10);
    }

    [TestMethod]
    public void PhysicalClearanceIncludesOnlyTheCurvedPartOfEachExtension()
    {
        var front = new SphericalSurface { Radius = 10, SemiAperture = 2, CurveSemiAperture = 4 };
        // sag(2) = 0.202, sag(4) = 0.835. A collision confined to the extension must be detected.
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            SurfaceClearance.ValidatePositiveThickness(front, OpticalSurface.Flat(), 0.5, 2));
        SurfaceClearance.ValidatePositiveThickness(front, OpticalSurface.Flat(), 0.9, 2);
        // Unspecified back-surface radius stays at the assembly clear aperture while the front continues.
        var back = OpticalSurface.Sphere(-10);
        SurfaceClearance.ValidatePositiveThickness(front, back, 1.1, 2);
        front.CurveSemiAperture = 1.5;
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            SurfaceClearance.ValidatePositiveThickness(front, back, 1.1, 2));
    }

    [TestMethod]
    public void ParabolicExtensionUsesTheConicInsteadOfASphericalRadiusLimit()
    {
        var surface = new AsphericSurface { Radius = 4, ConicConstant = -1, SemiAperture = 3.5, CurveSemiAperture = 5 };
        Assert.AreEqual(5, OpticSurfaceGeometry.DrawnSemiAperture(surface, 4), 1e-10);
        Assert.AreEqual(25.0 / 8, surface.Sag(5), 1e-10);
        SurfaceClearance.ValidatePositiveThickness(surface, OpticalSurface.Flat(), 4, 3.5);
    }
}
