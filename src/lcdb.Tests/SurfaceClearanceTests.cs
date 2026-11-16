using System;
using lcdb.Optic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace lcdb.Tests;

[TestClass]
public class SurfaceClearanceTests
{
    private static AsphericSurface NarrowMinimum(double minimumThickness)
    {
        double squaredMinimumRadius = 0.5125 * 0.5125;
        double quartic = (1 - minimumThickness) / (squaredMinimumRadius * squaredMinimumRadius);
        return new AsphericSurface
        {
            Radius = double.PositiveInfinity,
            QuadraticCoefficient = -2 * quartic * squaredMinimumRadius,
            EvenCoefficients = new[] { quartic },
        };
    }

    [TestMethod]
    public void DetectsIntersectionBetweenTheOldFortyIntervals()
    {
        var back = NarrowMinimum(-0.0001);
        for (int index = 0; index <= 40; index++)
            Assert.IsTrue(1 + back.Sag(index / 40.0) > 0, "旧采样点应全部为正。");
        Assert.AreEqual(-0.0001, 1 + back.Sag(0.5125), 1e-12);
        Assert.ThrowsException<InvalidOperationException>(() =>
            SurfaceClearance.ValidatePositiveThickness(OpticalSurface.Flat(), back, 1, 1));
    }

    [TestMethod]
    public void AcceptsPositiveNonMonotonicAsphericThickness()
    {
        var back = NarrowMinimum(0.0001);
        SurfaceClearance.ValidatePositiveThickness(OpticalSurface.Flat(), back, 1, 1);
    }

    [TestMethod]
    public void AcceptsOrdinaryBiconvexLens()
    {
        SurfaceClearance.ValidatePositiveThickness(OpticalSurface.Sphere(50), OpticalSurface.Sphere(-50), 5, 10);
    }

    [TestMethod]
    [DataRow(50.0)]
    [DataRow(-50.0)]
    public void MatchingCurvedFacesRetainVerySmallPositiveThickness(double radius)
    {
        SurfaceClearance.ValidatePositiveThickness(OpticalSurface.Sphere(radius), OpticalSurface.Sphere(radius), 1e-10, 10);
    }

    [TestMethod]
    public void CommonConicBaseCancelsBeforePolynomialBounds()
    {
        var front = new AsphericSurface { Radius = 50, ConicConstant = -1, QuadraticCoefficient = 1e-4, EvenCoefficients = new[] { 1e-7 } };
        var back = (AsphericSurface)front.Clone();
        back.EvenCoefficients[0] += 1e-9;
        SurfaceClearance.ValidatePositiveThickness(front, back, 1e-10, 10);
    }

    [TestMethod]
    public void PerSurfaceApertureClampsToAFlatShoulder()
    {
        var front = new SphericalSurface { Radius = 5, SemiAperture = 3 };
        SurfaceClearance.ValidatePositiveThickness(front, OpticalSurface.Flat(), 1.2, 6);
        Assert.ThrowsException<InvalidOperationException>(() =>
            SurfaceClearance.ValidatePositiveThickness(front, OpticalSurface.Flat(), 0.9, 6));
    }

    [TestMethod]
    public void SphericalAndConicRimsAreCheckedBeforeEqualShapesCancel()
    {
        var sphere = OpticalSurface.Sphere(5);
        SurfaceClearance.ValidatePositiveThickness(sphere, sphere.Clone(), 0.1, 5);
        Assert.ThrowsException<InvalidOperationException>(() =>
            SurfaceClearance.ValidatePositiveThickness(sphere, sphere.Clone(), 0.1, 5.001));
        var asphere = new AsphericSurface { Radius = 10, ConicConstant = 3 };
        SurfaceClearance.ValidatePositiveThickness(asphere, asphere.Clone(), 0.1, 5);
        Assert.ThrowsException<InvalidOperationException>(() =>
            SurfaceClearance.ValidatePositiveThickness(asphere, asphere.Clone(), 0.1, 5.001));
    }

    [TestMethod]
    public void EachCementedPieceRequiresItsOwnPositiveThickness()
    {
        var lens = new CementedLens
        {
            FrontSurface = OpticalSurface.Flat(), ContactSurface = OpticalSurface.Sphere(10), BackSurface = OpticalSurface.Flat(),
            T1 = 1, T2 = 0.1, Diameter = 4,
        };
        SurfaceClearance.ValidatePositiveThickness(lens.FrontSurface, lens.BackSurface, lens.T1 + lens.T2, lens.Diameter / 2);
        SurfaceClearance.ValidatePositiveThickness(lens.FrontSurface, lens.ContactSurface, lens.T1, lens.Diameter / 2);
        Assert.ThrowsException<InvalidOperationException>(() =>
            SurfaceClearance.ValidatePositiveThickness(lens.ContactSurface, lens.BackSurface, lens.T2, lens.Diameter / 2));
        lens.T2 = 0.3;
        SurfaceClearance.ValidatePositiveThickness(lens.ContactSurface, lens.BackSurface, lens.T2, lens.Diameter / 2);
    }

    [TestMethod]
    public void ZeroClearanceAndNonfiniteGeometryAreNeverAccepted()
    {
        var tangent = new AsphericSurface { QuadraticCoefficient = -8, EvenCoefficients = new[] { 16.0 } };
        Assert.ThrowsException<InvalidOperationException>(() =>
            SurfaceClearance.ValidatePositiveThickness(OpticalSurface.Flat(), tangent, 1, 1));
        var invalid = new AsphericSurface { QuadraticCoefficient = double.NaN };
        Assert.ThrowsException<InvalidOperationException>(() =>
            SurfaceClearance.ValidatePositiveThickness(invalid, invalid.Clone(), 1, 1));
        var overflow = new AsphericSurface { EvenCoefficients = new[] { double.MaxValue } };
        Assert.ThrowsException<InvalidOperationException>(() =>
            SurfaceClearance.ValidatePositiveThickness(overflow, overflow.Clone(), 1, 2));
    }
}
