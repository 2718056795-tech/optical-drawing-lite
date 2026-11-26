using System;
using System.Collections.Generic;
using System.Linq;
using LitMath;
using lcdb.Annotation;
using lcdb.NetDxfAdapter;
using lcdb.Optic;
using lcdb.Optic.Import;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace lcdb.Tests;

[TestClass]
public sealed class Release113ImportTests
{
    private static string Seq(string middle, string prefix = "RDM;LEN", string zoom = "") =>
        prefix + "\nDIM M\nSO 0 INF\n" + middle + "\nSI 0 0\n" + zoom + "\nGO";

    [TestMethod]
    public void CurvatureAndRadiusModesApplyInSourceOrder()
    {
        var data = new CodeVSeqParser().ParseText(Seq("S .02 4 N-BK7\nRDM YES\nS 80 3\nCUY S2 -.025", "RDM NO;LEN"));
        Assert.IsTrue(data.IsValid, string.Join("\n", data.Errors));
        Assert.AreEqual(50, data.Surfaces[1].Radius, 1e-12);
        Assert.AreEqual(-40, data.Surfaces[2].Radius, 1e-12);
        var plane = new CodeVSeqParser().ParseText(Seq("S 80 4 N-BK7\nCUY 0\nS -60 3"));
        Assert.IsTrue(double.IsPositiveInfinity(plane.Surfaces[1].Radius));
        var cm = new CodeVSeqParser().ParseText("RDM NO;LEN\nDIM C\nSO 0 INF\nS -.5 .3 N-BK7\nS .25 1\nRDY S2 -3\nSI 0 0\nGO");
        Assert.AreEqual(-20, cm.Surfaces[1].Radius, 1e-12);
        Assert.AreEqual(-30, cm.Surfaces[2].Radius, 1e-12);
    }

    [TestMethod]
    public void ZoomedCurvatureAndAsphericTermsUseSelectedConfiguration()
    {
        var seq = Seq("S 70 4 N-BK7\nASP\nA 0.2\nC 2e-9\nCUY -.1\nS -80 3", zoom:
            "ZOO 3\nZOO TIT\nZOO CUY S1 0.02 .025 .05\nZOO A S1 .0001 .0008 .0005\nZOO B S1 0.02\nZOO H S1\nH S1 Z2 4e-21\nTHI S1 3 N 7");
        var data = new CodeVSeqParser().ParseText(seq, 2);
        Assert.IsTrue(data.IsValid, string.Join("\n", data.Errors));
        Assert.AreEqual(40, data.Surfaces[1].Radius, 1e-12);
        Assert.AreEqual(4, data.Surfaces[1].Thickness);
        var coeffs = data.Surfaces[1].AsphericCoefficients;
        Assert.AreEqual(.0008, coeffs[0]);
        Assert.AreEqual(.02, coeffs[1]);
        Assert.AreEqual(2e-9, coeffs[2]);
        Assert.AreEqual(4e-21, coeffs[7]);
        var first = new CodeVSeqParser().ParseText(seq);
        Assert.AreEqual(.0001, first.Surfaces[1].AsphericCoefficients[0]);
        Assert.AreEqual(0, first.Surfaces[1].AsphericCoefficients[7]);
    }

    [TestMethod]
    [DataRow("CUY S1 INF")]
    [DataRow("ZOO 2\nZOO A S1 1 2 3")]
    [DataRow("ZOO 2\nZOO CUY S99 1 2")]
    [DataRow("RDM WHATEVER")]
    [DataRow("CUX S1 .1")]
    public void UnreadableGeometryIsReported(string extra)
    {
        var data = new CodeVSeqParser().ParseText(Seq("S 70 4 N-BK7\nS -80 3", zoom: extra));
        Assert.IsFalse(data.IsValid);
    }

    [TestMethod]
    [DataRow("35.25", true)]
    [DataRow("\"-35.25\"", true)]
    [DataRow("null", true)]
    [DataRow("\"bad\"", false)]
    [DataRow("true", false)]
    [DataRow("{}", false)]
    [DataRow("1e400", false)]
    [DataRow("0", false)]
    public void JsonRadiusMustBeKnownOrExplicitPlane(string radius, bool valid)
    {
        var json = "{\"surface_group\":{\"surfaces\":[{}, {\"geometry\":{\"radius\":" + radius + "}}, {}]}}";
        var data = new OpticJsonDeserializer().Parse(json);
        Assert.AreEqual(valid, data.IsValid, string.Join("\n", data.Errors));
        if (!valid) StringAssert.Contains(string.Join("\n", data.Errors), "S1 geometry.radius");
        else if (radius == "null") Assert.IsTrue(double.IsPositiveInfinity(data.Surfaces[1].Radius));
        else Assert.AreEqual(radius.Contains("-") ? -35.25 : 35.25, data.Surfaces[1].Radius);
    }

    [TestMethod]
    public void OriginalNumberCountsUnsupportedGroupsAndRetainsSourceSurfaceNumbers()
    {
        var data = new CodeVSeqParser().ParseText(Seq("S 50 3 N-BK7\nS 40 3 N-BK7\nS 60 3 N-BK7\nS -90 10\nS 50 3 N-BK7\nCIR EDG 3\nS -40 2", zoom:"ZOO 2"), 2);
        data.Surfaces[5].SurfaceNumber = 15;
        data.Surfaces[6].SurfaceNumber = 17;
        var result = new PrescriptionToLensBuilder().Build(data);
        var part = (OpticalLens)result.Entities.Single();
        Assert.AreEqual(1, result.SkippedCount);
        Assert.AreEqual(2, ImportIdentity.ElementNumber(part, -1));
        Assert.AreEqual("S15/S17", ImportIdentity.Tag(part, "sourceSurfaces"));
        Assert.AreEqual("Z2", ImportIdentity.Configuration(part));
        Assert.AreEqual(6d, part.MechanicalDiameter.Value);
    }

    private sealed class RecordedText : MockGraphicsDraw, OtoCAD.IGraphicsDraw
    {
        public readonly List<string> Lines = new();
        public new Vector2 DrawText(Vector2 p, string text, double h, string font, TextAlignment al, double angle)
        { Lines.Add(text); return p; }
        public new Vector2 DrawText(Vector3 p, string text, double h, string font, TextAlignment al, double angle)
        { Lines.Add(text); return new Vector2(p.X, p.Y); }
    }

    [TestMethod]
    public void DrawnAsphereShowsRoundTrippableSmallK()
    {
        var block = AsphericDataBlock.FromSurface(new AsphericSurface { Radius = 50.123456789, ConicConstant = -0.0004 }, 8, "1", Vector2.Zero);
        var recorder = new RecordedText();
        block.Draw(recorder);
        CollectionAssert.Contains(recorder.Lines, "K = -0.0004");
        CollectionAssert.Contains(recorder.Lines, "R = 50.123456789");
    }
}
