using System.Linq;
using lcdb.Optic;
using lcdb.Optic.Import;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace lcdb.Tests;

[TestClass]
public class ZemaxChipZoneTests
{
    private static ZmxSystemData Parse(string front, string back, string unit = "MM", bool cemented = false)
    {
        string middle = cemented
            ? "SURF 2\nTYPE STANDARD\nCURV -0.02\nDISZ 2\nGLAS N-F2\nDIAM 5\nOEMA 0.3\nMEMA 7\n"
            : "";
        int final = cemented ? 3 : 2;
        return new ZmxParser().ParseContent($"MODE SEQ\nUNIT {unit}\nSURF 0\nDISZ INFINITY\n" +
            $"SURF 1\nTYPE STANDARD\nCURV 0.02\nDISZ 4\nGLAS N-BK7\n{front}\n{middle}" +
            $"SURF {final}\nTYPE EVENASPH\nCURV -0.015\nPARM 2 0.000001\n{back}\n" +
            $"SURF {final + 1}\nTYPE STANDARD\n");
    }

    [TestMethod]
    [DataRow("MM", 1.0)]
    [DataRow("CM", 10.0)]
    [DataRow("IN", 25.4)]
    public void ChipZoneIsARadialWidthAndAllThreeRadiiUseLensUnits(string unit, double scale)
    {
        var data = Parse("DIAM 4\nOEMA 0.75 0 0 0 1 \"\"\nMEMA 4.75",
            "DIAM 3\nFLAP 0 3\nOEMA 0.25\nMEMA 4.75", unit);
        Assert.IsTrue(data.IsValid, string.Join("\n", data.Errors));
        var lens = (OpticalLens)new PrescriptionToLensBuilder().Build(data).Entities.Single();
        Assert.AreEqual(0.75 * scale, data.Surfaces[1].Clone().ChipZone, 1e-10);
        Assert.AreEqual(4 * scale, lens.FrontSurface.SemiAperture, 1e-10);
        Assert.AreEqual(4.75 * scale, lens.FrontSurface.CurveSemiAperture!.Value, 1e-10);
        Assert.AreEqual(3 * scale, lens.BackSurface.SemiAperture, 1e-10);
        Assert.AreEqual(3.25 * scale, lens.BackSurface.CurveSemiAperture!.Value, 1e-10);
        Assert.AreEqual(lens.FrontSurface.Sag(4.75 * scale), data.Surfaces[1].EdgeSag, 1e-10);
        Assert.AreEqual(lens.BackSurface.Sag(3.25 * scale), data.Surfaces[2].EdgeSag, 1e-10);
        Assert.AreEqual(8 * scale, lens.Diameter, 1e-10);
        Assert.AreEqual(9.5 * scale, lens.MechanicalDiameter!.Value, 1e-10);
    }

    [TestMethod]
    public void CircularApertureSetsThePhysicalExtensionBaseOnEachFaceOfADoublet()
    {
        var data = Parse("DIAM 5\nCLAP 0 6\nOEMA 1\nMEMA 7",
            "DIAM 6\nCLAP 0 4\nOEMA 0.5\nMEMA 7", cemented: true);
        Assert.IsTrue(data.IsValid);
        var part = (CementedLens)new PrescriptionToLensBuilder().Build(data).Entities.Single();
        Assert.AreEqual(5d, part.FrontSurface.SemiAperture);
        Assert.AreEqual(7d, part.FrontSurface.CurveSemiAperture!.Value);
        Assert.AreEqual(5.3, part.ContactSurface.CurveSemiAperture!.Value, 1e-10);
        Assert.AreEqual(4d, part.BackSurface.SemiAperture);
        Assert.AreEqual(4.5, part.BackSurface.CurveSemiAperture!.Value, 1e-10);
    }

    [TestMethod]
    public void AbsentMechanicalEdgeUsesTheCurveAndZeroChipKeepsTheFlat()
    {
        var data = Parse("DIAM 5\nOEMA 1", "DIAM 5\nOEMA 0\nMEMA 5.5");
        var part = (OpticalLens)new PrescriptionToLensBuilder().Build(data).Entities.Single();
        Assert.AreEqual(12d, part.MechanicalDiameter!.Value);
        Assert.AreEqual(6d, part.FrontSurface.CurveSemiAperture!.Value);
        Assert.IsNull(part.BackSurface.CurveSemiAperture);
        Assert.AreEqual(5d, part.BackSurface.SemiAperture);
    }

    [TestMethod]
    [DataRow(false, "MEMA 6", true, 12d)]
    [DataRow(true, "MEMA 6", true, 14d)]
    [DataRow(false, "MEMA 5.0000001", false, 10.0000002d)]
    [DataRow(false, "MEMA 5", false, 10d)]
    [DataRow(false, "", false, 10d)]
    public void ReportsOnlyDifferentExplicitMechanicalRadii(bool cemented, string backMema, bool expected, double outer)
    {
        var data = Parse("DIAM 4\nMEMA 5", "DIAM 4\n" + backMema, cemented: cemented);
        var result = new PrescriptionToLensBuilder().Build(data);
        var part = result.Entities.Single();
        double actual = part is OpticalLens lens ? lens.MechanicalDiameter!.Value : ((CementedLens)part).MechanicalDiameter!.Value;
        Assert.AreEqual(outer, actual, 1e-10);
        var reports = result.Warnings.Where(w => w.Contains("共用最大外径")).ToArray();
        Assert.AreEqual(expected ? 1 : 0, reports.Length);
        if (!expected) return;
        StringAssert.Contains(reports[0], "元件1");
        StringAssert.Contains(reports[0], "S1=5");
        StringAssert.Contains(reports[0], cemented ? "S2=7、S3=6" : "S2=6");
        StringAssert.Contains(reports[0], cemented ? "Ø14 mm" : "Ø12 mm");
    }

    [TestMethod]
    [DataRow("OEMA")]
    [DataRow("OEMA -0.5")]
    [DataRow("OEMA NaN")]
    public void InvalidRadialWidthsIdentifyTheSurfaceAndField(string row)
    {
        var data = Parse("DIAM 5\n" + row, "DIAM 5");
        Assert.IsFalse(data.IsValid);
        StringAssert.Contains(string.Join("\n", data.Errors), "S1");
        StringAssert.Contains(string.Join("\n", data.Errors), "OEMA");
    }

    [TestMethod]
    [DataRow("DIAM 5\nOEMA 1\nMEMA 5.5")]
    [DataRow("OEMA 1\nMEMA 6")]
    public void AmbiguousNonzeroZoneSkipsItsLensWithSourceReason(string fields)
    {
        var data = Parse(fields, "DIAM 5");
        Assert.IsTrue(data.IsValid);
        var result = new PrescriptionToLensBuilder().Build(data);
        Assert.AreEqual(0, result.Entities.Count);
        StringAssert.Contains(result.SkippedElements.Single(), "S1");
        StringAssert.Contains(result.SkippedElements.Single(), "OEMA");
    }
}
