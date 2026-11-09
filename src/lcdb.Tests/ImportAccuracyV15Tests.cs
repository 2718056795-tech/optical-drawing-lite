using System;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using lcdb.Optic;
using lcdb.Optic.Import;

namespace lcdb.Tests;

[TestClass]
public class ImportAccuracyV15Tests
{
    private static string Lens(string unit="MM", string front="TYPE EVENASPH\nCURV 0.02\nPARM 1 0.00001\nPARM 2 0.000001\nPARM 3 -0.000000002", string glass="N-BK7 0 0 1.5168 64.17") =>
        $"VERS 240529\nMODE SEQ\nUNIT {unit} X W X CM MR CPMM\nENPD 8\nWAVM 1 0.55 1\nSURF 0\nTYPE STANDARD\nDISZ INFINITY\nSURF 1\n{front}\nDISZ 5\nGLAS {glass}\nDIAM 5\nMEMA 6\nCLAP 0 4.5\nSURF 2\nTYPE STANDARD\nCURV -0.02\nDISZ 2\nDIAM 5\nMEMA 6\nSURF 3\nTYPE STANDARD\nCURV 0\nDISZ 0\n";

    private static OpticalLens Build(ZmxSystemData data)
    {
        Assert.IsTrue(data.IsValid,string.Join(";",data.Errors));
        return new PrescriptionToLensBuilder().Build(data).Entities.OfType<OpticalLens>().Single();
    }

    [TestMethod]
    public void EvenAsphereMapsA2A4A6ToIndependentExpectedSag()
    {
        var data=new ZmxParser().ParseContent(Lens());var lens=Build(data);
        var a=(AsphericSurface)lens.FrontSurface;
        Assert.AreEqual(1e-5,a.QuadraticCoefficient,1e-20);
        Assert.AreEqual(1e-6,a.EvenCoefficients[0],1e-20);
        Assert.AreEqual(-2e-9,a.EvenCoefficients[1],1e-23);
        foreach(double h in new[]{0d,1,2.5,4.5})
        {
            double expected=50-Math.Sqrt(2500-h*h)+1e-5*h*h+1e-6*Math.Pow(h,4)-2e-9*Math.Pow(h,6);
            Assert.AreEqual(expected,a.Sag(h),1e-12);
            Assert.AreEqual(expected,data.Surfaces[1].CalculateSag(h),1e-12);
        }
    }

    [TestMethod]
    public void ZeroBaseCurvatureRetainsPolynomialThroughCloneAndJson()
    {
        var lens=Build(new ZmxParser().ParseContent(Lens(front:"TYPE EVENASPH\nCURV 0\nPARM 1 0.00001\nPARM 2 0.000001")));
        var a=(AsphericSurface)lens.FrontSurface;
        double expected=1e-5*25+1e-6*625;
        Assert.AreEqual(expected,a.Sag(5),1e-15);
        Assert.AreEqual(expected,((AsphericSurface)a.Clone()).Sag(5),1e-15);
        var opt=new JsonSerializerOptions{NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals};
        var copy=JsonSerializer.Deserialize<OpticalSurface>(JsonSerializer.Serialize<OpticalSurface>(a,opt),opt);
        Assert.IsInstanceOfType<AsphericSurface>(copy);
        Assert.AreEqual(expected,copy.Sag(5),1e-15);
    }

    [TestMethod]
    [DataRow("MM",1d)] [DataRow("CM",10d)] [DataRow("M",1000d)] [DataRow("IN",25.4d)]
    public void UnitsConvertGeometryAndSagTogether(string unit,double factor)
    {
        var d=new ZmxParser().ParseContent(Lens(unit));var lens=Build(d);var s=d.Surfaces[1];
        Assert.AreEqual(50*factor,s.Radius,1e-9);Assert.AreEqual(5*factor,s.Thickness,1e-9);
        Assert.AreEqual(5*factor,s.SemiDiameter,1e-9);Assert.AreEqual(6*factor,s.MechanicalSemiDiameter.Value,1e-9);Assert.AreEqual(4.5*factor,s.ClearSemiDiameter.Value,1e-9);
        Assert.AreEqual(8*factor,d.SystemAperture,1e-9);Assert.AreEqual(0.55,d.Wavelengths[0],1e-12);
        double h=4.5,expected=50-Math.Sqrt(2500-h*h)+1e-5*h*h+1e-6*Math.Pow(h,4)-2e-9*Math.Pow(h,6);
        Assert.AreEqual(expected*factor,lens.FrontSurface.Sag(h*factor),1e-9);
    }

    [TestMethod]
    public void UnitsMustBeExplicitAndSupported()
    {
        Assert.IsFalse(new ZmxParser().ParseContent(Lens().Replace("UNIT MM X W X CM MR CPMM\n","")).IsValid);
        Assert.IsFalse(new ZmxParser().ParseContent(Lens("FURLONG")).IsValid);
        Assert.IsFalse(new ZmxParser().ParseContent(Lens()+"UNIT IN\n").IsValid);
    }

    [TestMethod]
    public void MultipleConfigurationsRejectInsteadOfSilentlyImportingBase()
    {
        var p=new ZmxParser();Assert.IsFalse(p.ParseContent(Lens()+"MNUM 2 1\n").IsValid);
        Assert.IsFalse(p.ParseContent(Lens()+"MCON THIC 1 2 3\n").IsValid);
        Assert.IsTrue(p.ParseContent(Lens()+"MNUM 1 1\n").IsValid);
    }

    [TestMethod]
    public void FixedCatalogGlassUsesCatalogAndIgnoresInactiveFields()
    {
        var data=new ZmxParser().ParseContent(Lens(glass:"N-BK7 0 0 1.5 40"));
        var result=new PrescriptionToLensBuilder().Build(data);var l=(OpticalLens)result.Entities.Single();
        Assert.AreEqual(1.5168,l.RefractiveIndex,1e-10);Assert.AreEqual(64.17,l.AbbeNumber,1e-10);
        Assert.AreEqual("1.5",l.Extensions.Tags["sourceNd1"]);Assert.AreEqual("40",l.Extensions.Tags["sourceVd1"]);
        Assert.AreEqual("catalog",l.Extensions.Tags["materialOpticsSource1"]);
        Assert.IsFalse(result.Warnings.Any(x=>x.StartsWith("材料", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void GcatVendorBoundaryAndUnknownGlassDoNotInheritDefaultIndices()
    {
        var data=new ZmxParser().ParseContent(Lens(glass:"N-BK7 0 0 1.5 40")+"GCAT CDGM-ZEMAX2023NEW2\n");
        var b=new PrescriptionToLensBuilder().Build(data);var l=(OpticalLens)b.Entities.Single();
        Assert.IsTrue(double.IsNaN(l.RefractiveIndex));Assert.IsTrue(double.IsNaN(l.AbbeNumber));
        Assert.AreEqual("unresolved",l.Extensions.Tags["materialOpticsSource1"]);
        Assert.IsTrue(b.Warnings.Any(x=>x.Contains("材料未匹配")));
        var unknown=Build(new ZmxParser().ParseContent(Lens(glass:"UNLISTED_GLASS")));
        Assert.IsTrue(double.IsNaN(unknown.RefractiveIndex));Assert.IsTrue(double.IsNaN(unknown.AbbeNumber));
    }

    [TestMethod]
    public void ModelGlassUsesItsExplicitIndicesEvenWhenNameMatchesACatalog()
    {
        var lens = Build(new ZmxParser().ParseContent(Lens(glass: "N-BK7 1 0 1.5 40")));
        Assert.AreEqual(1.5, lens.RefractiveIndex, 1e-10);
        Assert.AreEqual(40.0, lens.AbbeNumber, 1e-10);
        Assert.AreEqual("model", lens.Extensions.Tags["materialOpticsSource1"]);
    }

    [TestMethod]
    public void CodeVArrayMeaningRemainsA4First()
    {
        var data=new CodeVSeqParser().ParseText("RDM;LEN\nDIM M\nSO 0 1e13\nS 50 5 N-BK7\nASP\nA 1e-6\nB -2e-9\nCIR 4.5\nS -50 2\nCIR 4.5\nSI 0 0\nGO");
        var lens=Build(data);var a=(AsphericSurface)lens.FrontSurface;
        Assert.AreEqual(0d,a.QuadraticCoefficient);Assert.AreEqual(1e-6,a.EvenCoefficients[0],1e-20);
        double h=4.5,expected=50-Math.Sqrt(2500-h*h)+1e-6*Math.Pow(h,4)-2e-9*Math.Pow(h,6);
        Assert.AreEqual(expected,a.Sag(h),1e-12);
    }
}
