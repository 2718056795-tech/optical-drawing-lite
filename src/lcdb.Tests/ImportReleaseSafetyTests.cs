using System;
using System.Linq;
using lcdb.Optic;
using lcdb.Optic.Import;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace lcdb.Tests;

[TestClass]
public class ImportReleaseSafetyTests
{
    private static string Lens(string extraRecord = "", string catalog = "SCHOTT", string glass = "N-BK7 0 0 1.5 40") =>
        $"VERS 240529\nMODE SEQ\nUNIT MM\nGCAT {catalog}\n" +
        "SURF 0\nTYPE STANDARD\nDISZ INFINITY\n" +
        $"SURF 1\nTYPE EVENASPH\nCURV 0.02\nDISZ 5\nGLAS {glass}\nDIAM 5\n{extraRecord}\n" +
        "SURF 2\nTYPE STANDARD\nCURV -0.02\nDISZ 10\nDIAM 5\n" +
        "SURF 3\nTYPE STANDARD\nCURV 0\nDISZ 0\n";

    [TestMethod]
    [DataRow("CURV BAD_RADIUS")]
    [DataRow("CURV NaN")]
    [DataRow("CURV Infinity")]
    [DataRow("CURV")]
    [DataRow("DISZ invalid")]
    [DataRow("DISZ NaN")]
    [DataRow("DISZ 0")]
    [DataRow("DISZ -2")]
    [DataRow("DISZ INFINITY")]
    [DataRow("DIAM invalid")]
    [DataRow("DIAM -1")]
    [DataRow("MEMA Infinity")]
    [DataRow("MEMA -2")]
    [DataRow("CONI NaN")]
    [DataRow("CONI")]
    [DataRow("PARM 1 NaN")]
    [DataRow("PARM invalid 1")]
    [DataRow("PARM 0 0")]
    [DataRow("PARM 1")]
    [DataRow("CLAP 0")]
    [DataRow("CLAP 0 invalid")]
    [DataRow("FLAP -1 2")]
    [DataRow("CLAP 3 2")]
    public void InvalidSurfaceRecordIdentifiesItsLocationAndCannotBuild(string record)
    {
        string source = Lens(record);
        string[] lines = source.Split('\n');
        int frontStart = Array.IndexOf(lines, "SURF 1"), backStart = Array.IndexOf(lines, "SURF 2");
        int sourceLine = Array.IndexOf(lines, record, frontStart, backStart - frontStart) + 1;
        var data = new ZmxParser().ParseContent(source);
        Assert.IsFalse(data.IsValid, record);
        Assert.IsTrue(data.Errors.Any(error => error.Contains("行 " + sourceLine) && error.Contains("S1")),
            string.Join("\n", data.Errors));
        Assert.AreEqual(0, new PrescriptionToLensBuilder().Build(data).Entities.Count);
    }

    [TestMethod]
    public void ValidScientificNotationAndPlanarCurvatureRemainSupported()
    {
        var data = new ZmxParser().ParseContent(Lens("CURV 0\nPARM 1 1D-5\nMEMA 6\nCONI 0"));
        Assert.IsTrue(data.IsValid, string.Join("\n", data.Errors));
        Assert.IsTrue(double.IsPositiveInfinity(data.Surfaces[0].Thickness));
        Assert.IsTrue(double.IsPositiveInfinity(data.Surfaces[1].Radius));
        Assert.AreEqual(1e-5, data.Surfaces[1].QuadraticCoefficient, 1e-15);
        Assert.AreEqual(1, new PrescriptionToLensBuilder().Build(data).Entities.Count);
    }

    [TestMethod]
    public void UnknownExplicitCatalogCannotAdoptSameNamedBuiltInGlass()
    {
        var data = new ZmxParser().ParseContent(Lens(catalog: "USER_PRIVATE", glass: "F2 0 0 1.6 50"));
        var result = new PrescriptionToLensBuilder().Build(data);
        var lens = (OpticalLens)result.Entities.Single();
        Assert.IsTrue(double.IsNaN(lens.RefractiveIndex));
        Assert.IsTrue(double.IsNaN(lens.AbbeNumber));
        Assert.AreEqual("unresolved", lens.Extensions.Tags["materialOpticsSource1"]);
        Assert.IsFalse(lens.Extensions.Tags.ContainsKey("matchedCatalog1"));
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("材料未匹配") && warning.Contains("USER_PRIVATE")));
        Assert.IsNull(GlassLibrary.FindForCatalogs("F2", "USER_PRIVATE.AGF"));
    }

    [TestMethod]
    public void ExplicitKnownCatalogStillUsesItsFixedGlassAndIgnoresInactiveModelFields()
    {
        var data = new ZmxParser().ParseContent(Lens(catalog: "USER_PRIVATE SCHOTT"));
        var result = new PrescriptionToLensBuilder().Build(data);
        var lens = (OpticalLens)result.Entities.Single();
        Assert.AreEqual(1.5168, lens.RefractiveIndex, 1e-10);
        Assert.AreEqual(64.17, lens.AbbeNumber, 1e-10);
        Assert.AreEqual("catalog", lens.Extensions.Tags["materialOpticsSource1"]);
        Assert.IsFalse(result.Warnings.Any(warning => warning.StartsWith("材料", StringComparison.Ordinal)));
        Assert.IsNull(GlassLibrary.FindForCatalogs("F2_SCHOTT", "USER_PRIVATE"));
    }

    [TestMethod]
    [DataRow("CIR NaN")]
    [DataRow("CIR -1")]
    [DataRow("CIR EDG Infinity")]
    [DataRow("RDY NaN")]
    [DataRow("THI NaN")]
    [DataRow("THI INF")]
    [DataRow("K Infinity")]
    [DataRow("A 1e999")]
    public void CodeVNonfiniteOrNegativeGeometryCannotBecomeAnIllustrativeFallback(string record)
    {
        string source = "LEN\nDIM M\nSO 0 INF\nS 50 5 N-BK7\n" + record + "\nS -50 2\nCIR 5\nSI 0 0\nGO";
        var data = new CodeVSeqParser().ParseText(source);
        Assert.IsFalse(data.IsValid, record);
        Assert.IsTrue(data.Errors.Count > 0);
        Assert.AreEqual(0, new PrescriptionToLensBuilder().Build(data).Entities.Count);
    }

    [TestMethod]
    public void CodeVExplicitPlanarAndObjectInfinityRemainSupported()
    {
        var data = new CodeVSeqParser().ParseText("LEN\nDIM M\nSO 0 INF\nS INF 5 N-BK7\nCIR 5\nS -50 2\nCIR 5\nSI 0 0\nGO");
        Assert.IsTrue(data.IsValid, string.Join("\n", data.Errors));
        Assert.AreEqual(1, new PrescriptionToLensBuilder().Build(data).Entities.Count);
    }

    [TestMethod]
    [DataRow("DIAM 1e308")]
    [DataRow("MEMA 1e308")]
    [DataRow("DISZ 1e308")]
    [DataRow("CLAP 0 1e308")]
    public void UnitConversionOverflowIsAnExplicitSurfaceError(string record)
    {
        var data = new ZmxParser().ParseContent(Lens(record).Replace("UNIT MM", "UNIT CM"));
        Assert.IsFalse(data.IsValid);
        Assert.IsTrue(data.Errors.Any(error => error.Contains("S1") && error.Contains("换算为毫米")), string.Join("\n", data.Errors));
        Assert.AreEqual(0, new PrescriptionToLensBuilder().Build(data).Entities.Count);
    }

    [TestMethod]
    public void CodeVUnitConversionOverflowIsRejected()
    {
        var data = new CodeVSeqParser().ParseText("LEN\nDIM I\nSO 0 INF\nS 50 5 N-BK7\nCIR 1e308\nS -50 2\nCIR 5\nSI 0 0\nGO");
        Assert.IsFalse(data.IsValid);
        Assert.IsTrue(data.Errors.Any(error => error.Contains("S1") && error.Contains("CIR") && error.Contains("换算为毫米")));
    }

    [TestMethod]
    public void SystemApertureUnitConversionOverflowIsRejected()
    {
        var data = new ZmxParser().ParseContent((Lens() + "ENPD 1e308\n").Replace("UNIT MM", "UNIT CM"));
        Assert.IsFalse(data.IsValid);
        Assert.IsTrue(data.Errors.Any(error => error.Contains("ENPD") && error.Contains("换算为毫米")));
    }

    [TestMethod]
    [DataRow("1e2")]
    [DataRow("1D2")]
    public void SystemApertureParsesTheWholeScientificNumber(string number)
    {
        var data = new ZmxParser().ParseContent(Lens() + "ENPD " + number + "\n");
        Assert.IsTrue(data.IsValid, string.Join("\n", data.Errors));
        Assert.AreEqual(100.0, data.SystemAperture);
    }

    [TestMethod]
    [DataRow("radius")]
    [DataRow("thickness")]
    [DataRow("aperture")]
    [DataRow("mechanical")]
    [DataRow("conic")]
    [DataRow("A2")]
    [DataRow("A4")]
    [DataRow("position")]
    [DataRow("decimalBoundary")]
    [DataRow("diameterBoundary")]
    public void SharedBuilderRejectsGeometryOutsideTheEditorsDecimalRange(string field)
    {
        var data = new ZmxParser().ParseContent(Lens());
        var surface = data.Surfaces[1];
        switch (field)
        {
            case "radius": surface.Radius = 1e29; break;
            case "thickness": surface.Thickness = 1e29; break;
            case "aperture": surface.SemiDiameter = 1e29; break;
            case "mechanical": surface.MechanicalSemiDiameter = 1e29; break;
            case "conic": surface.Conic = 1e29; break;
            case "A2": surface.QuadraticCoefficient = 1e29; break;
            case "A4": surface.AsphericCoefficients[0] = 1e29; break;
            case "position": data.Surfaces[0].Thickness = 5e28; surface.Thickness = 5e28; break;
            case "decimalBoundary": surface.Radius = (double)decimal.MaxValue; break;
            case "diameterBoundary": surface.SemiDiameter = (double)decimal.MaxValue / 2; break;
        }
        var error = Assert.ThrowsExactly<FormatException>(() => new PrescriptionToLensBuilder().Build(data));
        StringAssert.Contains(error.Message, "decimal");
    }

    [TestMethod]
    public void FullDiameterMustRemainRepresentableAfterDoublingASemiDiameter()
    {
        var data = new ZmxParser().ParseContent(Lens("DIAM 5e28"));
        Assert.IsTrue(data.IsValid);
        Assert.ThrowsExactly<FormatException>(() => new PrescriptionToLensBuilder().Build(data));
        data = new ZmxParser().ParseContent(Lens("MEMA 5e28"));
        Assert.ThrowsExactly<FormatException>(() => new PrescriptionToLensBuilder().Build(data));
    }

    [TestMethod]
    public void SupportedFiniteSizesAreNotLimitedByTheFormerTenThousandMillimeterInputLimit()
    {
        var data = new ZmxParser().ParseContent(Lens("DIAM 100000").Replace("CURV 0.02", "CURV 0").Replace("CURV -0.02", "CURV 0"));
        var lens = (OpticalLens)new PrescriptionToLensBuilder().Build(data).Entities.Single();
        Assert.AreEqual(200000.0, lens.Diameter);
    }

    [TestMethod]
    public void TinyAsphericCoefficientsKeepTheirOriginalDoublePrecision()
    {
        var data = new ZmxParser().ParseContent(Lens());
        data.Surfaces[1].AsphericCoefficients[7] = 1e-40;
        var lens = (OpticalLens)new PrescriptionToLensBuilder().Build(data).Entities.Single();
        Assert.AreEqual(1e-40, ((AsphericSurface)lens.FrontSurface).EvenCoefficients[7]);
    }

    [TestMethod]
    public void SmallNonzeroCurvatureDoesNotBecomeAPlaneToBypassEditorLimits()
    {
        var data = new ZmxParser().ParseContent(Lens("CURV 1e-30"));
        Assert.IsTrue(data.IsValid);
        Assert.IsTrue(double.IsFinite(data.Surfaces[1].Radius));
        Assert.ThrowsExactly<FormatException>(() => new PrescriptionToLensBuilder().Build(data));
    }

    [TestMethod]
    public void ReciprocalCurvatureOverflowReportsTheSourceSurface()
    {
        var data = new ZmxParser().ParseContent(Lens("CURV 1e-320"));
        Assert.IsFalse(data.IsValid);
        Assert.IsTrue(data.Errors.Any(error => error.Contains("S1") && error.Contains("CURV") && error.Contains("倒数")));
    }

    [TestMethod]
    public void HugeFiniteCodeVRadiusDoesNotBecomeAnInfinitePlane()
    {
        var data = new CodeVSeqParser().ParseText("LEN\nDIM M\nSO 0 INF\nS 1e308 5 N-BK7\nCIR 5\nS -50 2\nCIR 5\nSI 0 0\nGO");
        Assert.IsTrue(data.IsValid, string.Join("\n", data.Errors));
        Assert.AreEqual(1e308, data.Surfaces[1].Radius);
        Assert.ThrowsExactly<FormatException>(() => new PrescriptionToLensBuilder().Build(data));
    }
}
