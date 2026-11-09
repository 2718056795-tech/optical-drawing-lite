using System.Collections.Generic;
using System.Linq;
namespace lcdb.DrawingFrame;

/// <summary>Persistent provenance for a scaled optical drawing. All geometric entity values remain paper units.</summary>
public sealed class OpticalDrawingBinding
{
    public string LastGeometrySignature { get; set; } = "";
    public double ViewScale { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Number { get; set; } = "";
    public string Source { get; set; } = "";
    public string LastMaterial1 { get; set; } = "";
    public string LastMaterial2 { get; set; } = "";
    public List<TemplateViewRegion> Regions { get; set; } = new();
    public OpticalDrawingBinding Copy()=>new(){LastGeometrySignature=LastGeometrySignature,ViewScale=ViewScale,Name=Name,Number=Number,Source=Source,LastMaterial1=LastMaterial1,LastMaterial2=LastMaterial2,Regions=Regions.Select(x=>(TemplateViewRegion)x.Clone()).ToList()};
}
public sealed class TemplateTextBinding
{
    public string Expression { get; set; } = "";
    public string LastRendered { get; set; } = "";
    public Dictionary<string,string> LastValues { get; set; } = new();
    public TemplateTextBinding Copy()=>new(){Expression=Expression,LastRendered=LastRendered,LastValues=new(LastValues)};
}
