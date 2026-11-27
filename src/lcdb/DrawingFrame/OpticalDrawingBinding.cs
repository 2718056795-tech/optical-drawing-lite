using System.Collections.Generic;
using System.Linq;
namespace lcdb.DrawingFrame;

/// <summary>Persistent provenance for a scaled optical drawing. All geometric entity values remain paper units.</summary>
public sealed class OpticalDrawingBinding
{
    public string LastGeometrySignature { get; set; } = "";
    public double ViewScale { get; set; } = 1;
    // Fixed-scale views retain the usable paper dimensions after annotations have been reserved.
    public double? FixedBodyWidth { get; set; }
    public double? FixedBodyHeight { get; set; }
    public string Name { get; set; } = "";
    public string DrawingDate { get; set; } = "";
    public string Number { get; set; } = "";
    public string Source { get; set; } = "";
    public string LastMaterial1 { get; set; } = "";
    public string LastMaterial2 { get; set; } = "";
    public List<TemplateViewRegion> Regions { get; set; } = new();
    public OpticalDrawingBinding Copy()=>new(){LastGeometrySignature=LastGeometrySignature,ViewScale=ViewScale,FixedBodyWidth=FixedBodyWidth,FixedBodyHeight=FixedBodyHeight,Name=Name,DrawingDate=DrawingDate,Number=Number,Source=Source,LastMaterial1=LastMaterial1,LastMaterial2=LastMaterial2,Regions=Regions.Select(x=>(TemplateViewRegion)x.Clone()).ToList()};
}
public sealed class TemplateTextBinding
{
    public string Expression { get; set; } = "";
    /// <summary>Explicit literal for this field of this drawing. Null means automatic; an empty string is a valid override.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? OverrideText { get; set; }
    public string LastRendered { get; set; } = "";
    public Dictionary<string,string> LastValues { get; set; } = new();
    public TemplateTextBinding Copy()=>new(){Expression=Expression,OverrideText=OverrideText,LastRendered=LastRendered,LastValues=new(LastValues)};
}
