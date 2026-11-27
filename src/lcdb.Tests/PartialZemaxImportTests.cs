using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using lcdb.Optic;
using lcdb.Optic.Import;

namespace lcdb.Tests;

[TestClass]
public sealed class PartialZemaxImportTests
{
    private static string Row(int number, string type = "STANDARD", string glass = "", double thickness = 2, string extra = "") =>
        $"SURF {number}\nTYPE {type}\nDISZ {thickness.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n" +
        (glass.Length > 0 ? "GLAS " + glass + "\n" : "") + "DIAM 3\n" + extra + "\n";
    private static string Lens(params string[] rows) =>
        "VERS 240529\nMODE SEQ\nUNIT MM\nSURF 0\nTYPE STANDARD\nDISZ INFINITY\n" + string.Concat(rows) + Row(99, thickness:0);
    private static PrescriptionToLensBuilder.Result Build(string file)
    {
        var data = new ZmxParser().ParseContent(file);
        Assert.IsTrue(data.IsValid, string.Join("\n", data.Errors));
        return new PrescriptionToLensBuilder().Build(data);
    }

    [TestMethod]
    [DataRow(0, "BINARY2")]
    [DataRow(1, "TOROIDAL")]
    [DataRow(2, "UNRECOGNIZED_ZEMAX_SURFACE")]
    public void UnsupportedInterfaceOmitsWholeCementedGroupAndPreservesOtherPartPositions(int location, string type)
    {
        string[] types = { "STANDARD", "STANDARD", "STANDARD" }; types[location] = type;
        var result = Build(Lens(Row(1,glass:"N-BK7"), Row(2,thickness:3),
            Row(5,types[0],"N-BK7",4), Row(7,types[1],"N-F2",5), Row(8,types[2],thickness:6),
            Row(10,glass:"N-BK7",thickness:8), Row(11)));
        Assert.AreEqual(2, result.SingleCount);
        Assert.AreEqual(0, result.CementedCount);
        Assert.AreEqual(1, result.SkippedCount);
        Assert.AreEqual(0, result.SkippedStandaloneSurfaces.Count);
        string issue = result.SkippedElements.Single();
        StringAssert.Contains(issue, "元件2");
        StringAssert.Contains(issue, "S5–S8");
        StringAssert.Contains(issue, new[] {"S5","S7","S8"}[location]);
        StringAssert.Contains(issue, type);
        var last = (OpticalLens)result.Entities[1];
        Assert.AreEqual(3, ImportIdentity.ElementNumber(last, -1));
        Assert.AreEqual("S10/S11", ImportIdentity.Tag(last, "sourceSurfaces"));
        Assert.AreEqual(24d, last.Position.X);
    }

    [TestMethod]
    public void IndependentCoordinateBreakAndAirMirrorDoNotConsumeEitherLens()
    {
        var result = Build(Lens(Row(1,glass:"N-BK7"),Row(2),Row(3,"COORDBRK",thickness:4,extra:"PARM 1 10"),
            Row(4,glass:"MIRROR",thickness:-1),Row(5,glass:"N-F2",thickness:6),Row(6)));
        Assert.AreEqual(2, result.SingleCount);
        Assert.AreEqual(0, result.SkippedCount);
        Assert.AreEqual(2, result.SkippedStandaloneSurfaces.Count);
        StringAssert.Contains(result.SkippedStandaloneSurfaces[0], "S3");
        StringAssert.Contains(result.SkippedStandaloneSurfaces[0], "COORDBRK");
        StringAssert.Contains(result.SkippedStandaloneSurfaces[1], "MIRROR");
        var last = (OpticalLens)result.Entities.Last();
        Assert.AreEqual(2, ImportIdentity.ElementNumber(last, -1));
        Assert.AreEqual(10d, last.Position.X);
    }

    [TestMethod]
    public void NegativeGlassThicknessInReflectedPathOmitsOnlyThatGroup()
    {
        var result = Build(Lens(Row(1,glass:"N-BK7"), Row(2), Row(3,glass:"MIRROR",thickness:-6),
            Row(4,glass:"N-F2",thickness:-3), Row(5,thickness:-2), Row(6,glass:"MIRROR",thickness:8),
            Row(7,glass:"N-BK7",thickness:4), Row(8)));
        Assert.AreEqual(2, result.SingleCount);
        Assert.AreEqual(1, result.SkippedCount);
        StringAssert.Contains(result.SkippedElements.Single(), "折返段");
        StringAssert.Contains(result.SkippedElements.Single(), "单片");
        var remaining = (OpticalLens)result.Entities[1];
        Assert.AreEqual(3, ImportIdentity.ElementNumber(remaining, -1));
        Assert.AreEqual(3d, remaining.Position.X);
    }

    [TestMethod]
    public void MirrorInGlassRetainsIncidentMediumUntilExitAndNextLensKeepsNumber()
    {
        var data = new ZmxParser().ParseContent(Lens(Row(1,glass:"N-BK7 0 0 1.5168 64.17"), Row(2,glass:"MIRROR",thickness:-2),
            Row(3,glass:"N-F2",thickness:-1), Row(4), Row(5,glass:"MIRROR"), Row(6,glass:"N-BK7"), Row(7)));
        Assert.IsTrue(data.IsValid, string.Join("\n",data.Errors));
        Assert.AreEqual("N-BK7", data.Surfaces[2].Glass);
        Assert.AreEqual(data.Surfaces[1].RefractiveIndexPost, data.Surfaces[2].RefractiveIndexPost);
        Assert.IsTrue(data.Surfaces[2].HasMaterial);
        Assert.IsFalse(data.Surfaces[5].HasMaterial);
        var result = new PrescriptionToLensBuilder().Build(data);
        Assert.AreEqual(1, result.SkippedCount);
        StringAssert.Contains(result.SkippedElements.Single(), "S1–S4");
        StringAssert.Contains(result.SkippedElements.Single(), "MIRROR");
        Assert.IsFalse(result.SkippedElements.Single().Contains("片胶合"));
        Assert.AreEqual("S6/S7", ImportIdentity.Tag(result.Entities.Single(),"sourceSurfaces"));
        Assert.AreEqual(2, ImportIdentity.ElementNumber(result.Entities.Single(),-1));
    }

    [TestMethod]
    public void TwoMirrorsInAirDoNotProduceAGlassGroup()
    {
        var result = Build(Lens(Row(1,glass:"N-BK7"), Row(2,glass:"AIR 0 0 1.5 40"), Row(3,glass:"MIRROR",thickness:-2),
            Row(4,glass:"MIRROR"), Row(5,glass:"N-F2"),Row(6)));
        Assert.AreEqual(2, result.SingleCount);
        Assert.AreEqual(0, result.SkippedCount);
        Assert.AreEqual(2, result.SkippedStandaloneSurfaces.Count);
        Assert.AreEqual(2,ImportIdentity.ElementNumber(result.Entities.Last(),0));
    }

    [TestMethod]
    [DataRow("EVENASPHERE")]
    [DataRow("ASPHERIC")]
    [DataRow("SPHERICAL")]
    [DataRow("CONIC")]
    public void UnsupportedZemaxAliasesCannotGenerateIncompleteSpheres(string token)
    {
        var data = new ZmxParser().ParseContent(Lens(Row(1,glass:"N-BK7"), Row(2),
            Row(3,token,"N-BK7",extra:"PARM 2 .0001"), Row(4), Row(5,glass:"N-BK7"), Row(6)));
        Assert.IsTrue(data.IsValid);
        var result = new PrescriptionToLensBuilder().Build(data);
        Assert.AreEqual(2,result.Entities.Count);
        StringAssert.Contains(result.SkippedElements.Single(),token);
        Assert.AreEqual(3,ImportIdentity.ElementNumber(result.Entities.Last(),0));
    }

    [TestMethod]
    public void AsphereParametersBeforeTypeUseFinalZemaxSurfaceType()
    {
        var file = Lens(Row(1,"EVENASPH","N-BK7"), Row(2)).Replace("TYPE EVENASPH","PARM 2 0.0001\nTYPE EVENASPH");
        var part = (OpticalLens)Build(file).Entities.Single();
        Assert.IsInstanceOfType<AsphericSurface>(part.FrontSurface);
        Assert.AreEqual(.0001, ((AsphericSurface)part.FrontSurface).EvenCoefficients[0]);
    }

    [TestMethod]
    public void BrokenShapeFieldsAndZeroGlassThicknessInsideOmittedGroupStayLocal()
    {
        var result = Build(Lens(Row(1,glass:"N-BK7",thickness:0,extra:"CURV bad"),
            Row(2,"COORDBRK",extra:"PARM 0 expression"),Row(3,glass:"N-F2"),Row(4)));
        StringAssert.Contains(result.SkippedElements.Single(), "CURV bad");
        StringAssert.Contains(result.SkippedElements.Single(), "PARM 0 expression");
        Assert.AreEqual(1,result.Entities.Count);
    }

    [TestMethod]
    public void OutOfRangeNonzeroAsphereCoefficientsOmitOnlyAffectedLens()
    {
        var result = Build(Lens(Row(1,"EVENASPH","N-BK7",extra:"PARM 9 7E-10"),Row(2),Row(3,glass:"N-F2"),Row(4)));
        StringAssert.Contains(result.SkippedElements.Single(),"PARM 9");
        Assert.AreEqual(2, ImportIdentity.ElementNumber(result.Entities.Single(), -1));
    }

    [TestMethod]
    public void AManuallyProvidedUnknownFaceCannotBeConvertedToASphere()
    {
        var data = new ZmxParser().ParseContent(Lens(Row(1,glass:"N-BK7"),Row(2)));
        data.Surfaces[1].Type = "OTHER";
        Assert.AreEqual(0,new PrescriptionToLensBuilder().Build(data).Entities.Count);
        Assert.ThrowsExactly<NotSupportedException>(()=>data.Surfaces[1].CalculateSag(1));
        StringAssert.Contains(data.Surfaces[1].Clone().UnsupportedReasons.Single(), "OTHER");
    }

    [TestMethod]
    public void UnknownSpacingModeAndMissingUnitStillStopEntireImport()
    {
        var file = Lens(Row(1,"TOROIDAL","N-BK7"),Row(2),Row(3,glass:"N-BK7"),Row(4));
        foreach (var broken in new[] {file.Replace("MODE SEQ","MODE NSC"),file.Replace("UNIT MM",""),file.Replace("DISZ 2","DISZ unknown")})
        {
            var data = new ZmxParser().ParseContent(broken);
            Assert.IsFalse(data.IsValid);
            Assert.AreEqual(0,new PrescriptionToLensBuilder().Build(data).Entities.Count);
        }
    }
}
