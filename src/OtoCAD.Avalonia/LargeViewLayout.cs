using System;
using System.Collections.Generic;
using System.Linq;
using lcdb;
using lcdb.DrawingFrame;
using LitMath;
namespace OtoCAD.Avalonia.Slim;

public static partial class TemplatePipeline
{
    static void ExpandAutoRegions(IReadOnlyList<Entity> entities)
    {
        var frame=entities.OfType<DrawingFrame>().FirstOrDefault(f=>f is IsoLensDrawingFrame||f is GbLensDrawingFrame);if(frame is null)return;
        double border=8,bottom=0;
        if(frame is IsoLensDrawingFrame i){border=i.BorderMargin;bottom=frame.Origin.Y+border+i.NotesRowHeight+i.TitleRowHeight+GroupHeight(i.Columns,i.SpecificationBands,i.TableRowHeight)+8;}
        else if(frame is GbLensDrawingFrame g){border=g.BorderMargin;bottom=frame.Origin.Y+border+g.TitleBlockHeightGb+GroupHeight(g.Columns,g.SpecificationBands,g.TableRowHeight)+Math.Max(16,g.NoteLines.Count*4.2+4)+8;}
        double left=frame.Origin.X+border+8,right=frame.Origin.X+frame.PaperWidth-border-8,top=frame.Origin.Y+frame.PaperHeight-border-8;
        foreach(var region in entities.OfType<TemplateViewRegion>().Where(r=>r.UseFullPaperArea&&r.ViewScale<=0))
        {
            if(top-bottom<55)throw new InvalidOperationException("参数表占用空间过多，请增大纸张或缩小表格。");
            region.Origin=new Vector2(left,bottom);region.PaperWidth=right-left;region.PaperHeight=top-bottom;
        }
    }
    static void PlaceDimensionsOutside(List<Entity> entities,Entity owner,double extensionGap=0.6)
    {
        var box=owner.bounding;
        foreach(var dim in entities.OfType<LinearDimension>().Where(d=>ReferenceEquals(d.Owner,owner)))
        {
            var st=dim.style;st.DimensionLineGap=1.3;st.ArrowSize=1.6;st.ExtensionLineExtend=1.2;
            var a=dim.firstReferencePoint;var b=dim.secondReferencePoint;double midY=(a.Y+b.Y)/2;
            if(Math.Abs(Math.Cos(dim.rotation))<1e-3)
            {
                double x=box.right+12;dim.offset=Math.Abs(x-(a.X+b.X)/2);st.ExtensionLineOffset=Math.Max(0,extensionGap);dim.Update();continue;
            }
            bool upper=dim.userText.StartsWith("(",StringComparison.Ordinal);double level=dim.userText.StartsWith("T2 ",StringComparison.Ordinal)?28:dim.userText.StartsWith("T1 ",StringComparison.Ordinal)?18:8;
            double y=upper?box.top+8:box.bottom-level;
            dim.offset=Math.Abs(y-midY);st.ExtensionLineOffset=Math.Max(0,extensionGap);
            double lo=Math.Min(a.X,b.X),hi=Math.Max(a.X,b.X),span=hi-lo;
            double textWidth=Math.Max(4,dim.userText.Length*st.TextHeight*.62),xText=(lo+hi)/2;
            if(span<textWidth+6)
            {
                bool right=dim.userText.StartsWith("T2 ",StringComparison.Ordinal);xText=right?hi+6+textWidth/2:lo-6-textWidth/2;dim.ExtendDimensionLineToText=true;
            }
            dim.textReferencePoint=new Vector2(xText,y+3.0);dim.Update();
        }
    }
}
