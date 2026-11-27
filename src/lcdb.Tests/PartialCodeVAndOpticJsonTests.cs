using System;
using System.Linq;
using System.Text.Json.Nodes;
using lcdb.Optic;
using lcdb.Optic.Import;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace lcdb.Tests;

[TestClass]
public sealed class PartialCodeVAndOpticJsonTests
{
    private static string Seq(string extra,string contact="S -70 3 N-F2") =>
        "RDM; LEN\nDIM M\nSO 0 INF\nS 40 4 N-BK7\nS -60 1\nS 50 2 H-LAK53B_CDGM\n"+
        contact+"\nS -100 1\nS 75 3 N-BK7\nS -90 1\nSI 0 0\n"+extra+"\nGO";

    private static PrescriptionToLensBuilder.Result Build(ZmxSystemData data)
    {
        Assert.IsTrue(data.IsValid,string.Join("\n",data.Errors));
        return new PrescriptionToLensBuilder().Build(data);
    }
    private static void CheckIsolatedDoublet(ZmxSystemData data,string why)
    {
        var result=Build(data);
        Assert.AreEqual(1,result.SkippedCount);
        Assert.AreEqual(2,result.SingleCount);
        Assert.AreEqual(0,result.CementedCount);
        var surviving=result.Entities.Cast<OpticalLens>().ToArray();
        CollectionAssert.AreEqual(new[]{1,3},surviving.Select(e=>ImportIdentity.ElementNumber(e,-1)).ToArray());
        CollectionAssert.AreEqual(new[]{"S1/S2","S6/S7"},surviving.Select(e=>ImportIdentity.Tag(e,"sourceSurfaces")).ToArray());
        Assert.AreEqual(40,surviving[0].R1,1e-9);
        Assert.AreEqual(75,surviving[1].R1,1e-9);
        Assert.AreEqual(3,surviving[1].Thickness,1e-9);
        StringAssert.Contains(result.SkippedElements.Single(),why);
    }

    [TestMethod]
    [DataRow("SPS S4 QBF","QBF")]
    [DataRow("SPS XYP S4","XYP")]
    [DataRow("XTO S4","XTO")]
    [DataRow("YTO S4","YTO")]
    [DataRow("SPL S4","SPL")]
    [DataRow("GRI S4","GRI")]
    [DataRow("CUX S4 .01","CUX")]
    [DataRow("DIF S4 DOE","DOE")]
    [DataRow("DIF SI-4 DOE","DOE")]
    [DataRow("NSS SO+3..I-3","NSS")]
    [DataRow("J S4 2e-30","A20")]
    [DataRow("NSS S3..5","NSS")]
    public void CodeVSkipsWholeDoubletWithAnUnsupportedSurface(string command,string why)
    {
        var data=new CodeVSeqParser().ParseText(Seq(command));
        CheckIsolatedDoublet(data,why);
        StringAssert.Contains(new PrescriptionToLensBuilder().Build(data).SkippedElements.Single(),"S4");
    }

    [TestMethod]
    public void NonstandardCodeVRadiusKeepsTheOriginalSourceRows()
    {
        var reader=new CodeVSeqParser();
        CheckIsolatedDoublet(reader.ParseText(Seq("SPS S4 QBF","S vendor_radius 3 N-F2")),"QBF");
        var good=reader.ParseText(Seq("","S vendor_radius 3 N-F2"));
        Assert.IsFalse(good.IsValid);
        StringAssert.Contains(string.Join("\n",good.Errors),"S4");
        var unknownSpacing=reader.ParseText(Seq("SPS S4 QBF","S 40 vendor_spacing N-F2"));
        Assert.IsFalse(unknownSpacing.IsValid);
    }

    [TestMethod]
    [DataRow("RDY S3 foo")]
    [DataRow("CUY S4 INF")]
    [DataRow("CIR S3 -1")]
    [DataRow("CIR S3 EDG -1")]
    [DataRow("K S4 invalid")]
    [DataRow("ZOO 2\\nZOO A S4 Z1 invalid")]
    public void CodeVLocalProfileDiagnosticsDoNotSuppressUnrelatedParts(string details)
    {
        string commands=details.Replace("\\n","\n");
        var reader=new CodeVSeqParser();
        CheckIsolatedDoublet(reader.ParseText(Seq("XTO S3\n"+commands)),"XTO");
        Assert.IsFalse(reader.ParseText(Seq(commands)).IsValid);
        Assert.IsFalse(reader.ParseText(Seq("XTO S3\nTHI S3 invalid")).IsValid);
    }

    [TestMethod]
    public void SelectedConfigurationCanKeepASupportedProfile()
    {
        var reader=new CodeVSeqParser();
        var text=Seq("ZOO 2\nZOO CUX S4 Z2 .01\nJ S4 1e-30\nZOO J S4 Z1 0");
        var one=Build(reader.ParseText(text,1));
        Assert.AreEqual(1,one.CementedCount);
        Assert.AreEqual(0,one.SkippedCount);
        var two=reader.ParseText(text,2);
        CheckIsolatedDoublet(two,"CUX");
        Assert.AreEqual("Z2",ImportIdentity.Configuration(Build(two).Entities.Last()));
    }

    [TestMethod]
    public void ImplicitSurfaceFollowsExplicitShapeTarget()
    {
        var data=new CodeVSeqParser().ParseText(Seq("ASP S1\nA 1e-6\nCYL S4\nRDY 123"));
        CheckIsolatedDoublet(data,"CYL");
        Assert.AreEqual(1e-6,data.Surfaces[1].AsphericCoefficients[0],1e-15);
        Assert.AreEqual(123,data.Surfaces[4].Radius,1e-10);
    }

    [TestMethod]
    public void StandaloneAirMirrorKeepsNextLensIndependent()
    {
        var text="RDM;LEN\nDIM M\nSO 0 INF\nS 0 10 TIRO\nS 75 3 N-BK7\nS -90 1\nSI 0 0\nGO";
        var result=Build(new CodeVSeqParser().ParseText(text));
        var lens=(OpticalLens)result.Entities.Single();
        Assert.AreEqual("S2/S3",ImportIdentity.Tag(lens,"sourceSurfaces"));
        StringAssert.Contains(result.SkippedStandaloneSurfaces.Single(),"S1");
    }

    [TestMethod]
    [DataRow("CYL S99")]
    [DataRow("CYL Sbad")]
    [DataRow("NSS S3..99")]
    [DataRow("NSS")]
    [DataRow("IF ^test")]
    [DataRow("ZOO 2\nZOO CUX S4 Z4 1")]
    public void UnknownSurfaceReferenceAndSystemCommandsRemainFatal(string command)
    {
        var data=new CodeVSeqParser().ParseText(Seq(command));
        Assert.IsFalse(data.IsValid);
        Assert.AreEqual(0,new PrescriptionToLensBuilder().Build(data).Entities.Count);
    }

    private static ZmxSystemData JsonData(Action<JsonObject> modify,Action<JsonArray> modifyAll=null)
    {
        var rows=new JsonArray();
        void Row(double? radius,int thickness,string material=null)
        {
            var row=new JsonObject{["type"]="Surface",["geometry"]=new JsonObject{["type"]="StandardGeometry",["radius"]=radius},
                ["thickness"]=thickness};
            if(material!=null)row["material_post"]=new JsonObject{["name"]=material,["type"]="Material"};
            rows.Add(row);
        }
        Row(null,0);Row(40,4,"N-BK7");Row(-60,1);Row(50,2,"H-LAK53B");Row(-70,3,"N-F2");Row(-100,1);
        Row(75,3,"N-BK7");Row(-90,1);Row(null,0);
        modify(rows[4].AsObject());
        modifyAll?.Invoke(rows);
        return new OpticJsonDeserializer().Parse(new JsonObject{["surface_group"]=new JsonObject{["surfaces"]=rows}}.ToJsonString());
    }

    [TestMethod]
    [DataRow("OddAsphere")]
    [DataRow("ToroidalGeometry")]
    [DataRow("FreeformVendorSag")]
    public void JsonCustomGeometryDoesNotProduceAnApproximateSphere(string kind)
    {
        var data=JsonData(row=>row["geometry"]=new JsonObject{["type"]=kind,["radius"]=new JsonArray(2,3)});
        CheckIsolatedDoublet(data,kind);
    }

    [TestMethod]
    public void JsonBadRadiusIsOnlyFatalForAGeometryThatWouldBeDrawn()
    {
        var partial=JsonData(row=>row["geometry"]["type"]="Toroid",rows=>rows[3]["geometry"]["radius"]="vendor-only");
        CheckIsolatedDoublet(partial,"Toroid");
        var invalid=JsonData(row=>row["geometry"]["type"]="Toroid",rows=>rows[6]["geometry"]["radius"]="not-a-radius");
        Assert.IsFalse(invalid.IsValid);
        StringAssert.Contains(string.Join("\n",invalid.Errors),"S6 geometry.radius");
    }

    [TestMethod]
    [DataRow("thickness")]
    [DataRow("material_post")]
    public void JsonUnknownAxisOrMaterialCannotBeSuppressedByAnUnsupportedShape(string field)
    {
        var data=JsonData(row=>{row["geometry"]["type"]="CustomFreeform";row[field]="unknown";});
        Assert.IsFalse(data.IsValid);
        StringAssert.Contains(string.Join("\n",data.Errors),"S4 "+field);
    }

    [TestMethod]
    public void JsonMirrorAndDiffractiveInteractionSkipTheOwningPart()
    {
        CheckIsolatedDoublet(JsonData(row=>row["interaction_model"]=new JsonObject{
            ["type"]="RefractiveReflectiveModel",["is_reflective"]=true}),"反射");
        CheckIsolatedDoublet(JsonData(row=>row["interaction_model"]=new JsonObject{
            ["type"]="DiffractiveInteractionModel",["is_reflective"]=false}),"Diffractive");
        CheckIsolatedDoublet(JsonData(row=>row.Remove("geometry")),"geometry");
    }

    [TestMethod]
    public void OptilandEvenTermsStartAtA2AndHighTermsCannotSilentlyDisappear()
    {
        void Even(JsonObject row,JsonArray coefficients)
        {
            row["geometry"]["type"]="EvenAsphere";
            row["geometry"]["coefficients"]=coefficients;
        }
        var data=JsonData(row=>Even(row,new JsonArray(.001,-.0001,.000005)));
        var lens=Build(data).Entities.OfType<CementedLens>().Single();
        var profile=(AsphericSurface)lens.ContactSurface;
        Assert.AreEqual(.001,profile.QuadraticCoefficient,1e-15);
        Assert.AreEqual(-.0001,profile.EvenCoefficients[0],1e-15);
        Assert.AreEqual(.000005,profile.EvenCoefficients[1],1e-15);
        CheckIsolatedDoublet(JsonData(row=>Even(row,new JsonArray(0,0,0,0,0,0,0,0,0,1e-28))),"A20");
        CheckIsolatedDoublet(JsonData(row=>Even(row,new JsonArray(0,"unreadable"))),"A4");
    }
}
