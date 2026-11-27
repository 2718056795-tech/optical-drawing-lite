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
        if(frame is IsoLensDrawingFrame i){border=i.BorderMargin;bottom=frame.Origin.Y+border+i.MeasuredNotesHeight()+i.TitleRowHeight+GroupHeight(i.Columns,i.SpecificationBands,i.TableRowHeight)+8;}
        else if(frame is GbLensDrawingFrame g){border=g.BorderMargin;bottom=frame.Origin.Y+border+g.TitleBlockHeightGb+GroupHeight(g.Columns,g.SpecificationBands,g.TableRowHeight)+g.MeasuredNotesHeight()+8;}
        double left=frame.Origin.X+border+8,right=frame.Origin.X+frame.PaperWidth-border-8,top=frame.Origin.Y+frame.PaperHeight-border-8;
        foreach(var region in entities.OfType<TemplateViewRegion>().Where(r=>r.UseFullPaperArea&&r.ViewScale<=0))
        {
            if(top-bottom<55)throw new InvalidOperationException("参数表占用空间过多，请增大纸张或缩小表格。");
            region.Origin=new Vector2(left,bottom);region.PaperWidth=right-left;region.PaperHeight=top-bottom;
        }
    }
    // Keep the vertical diameter and its rotated glyphs clear of actual, printed radius
    // callouts. Only move the diameter when a label falls in the same paper-space strip.
    private static double ClearRadiusCaptions(IEnumerable<Entity> entities, Entity owner, LinearDimension diameter, double x)
    {
        double low = Math.Min(diameter.firstReferencePoint.Y, diameter.secondReferencePoint.Y);
        double high = Math.Max(diameter.firstReferencePoint.Y, diameter.secondReferencePoint.Y);
        double captionMargin = diameter.EffectiveTextHeight + diameter.style.DimensionLineGap + 1.5;
        foreach (var radial in entities.OfType<RadialDimension>().Where(d => ReferenceEquals(d.Owner, owner) && d.TextAboveLanding))
        {
            var text = radial.GetDrawingPrimitives().OfType<Text>().FirstOrDefault();
            if (text is null || text.Position.Y - text.Height > high || text.Position.Y + text.Height < low) continue;
            double right = text.Position.X + text.HorizontalPaperWidth();
            if (text.Position.X - 1.5 <= x && x < right + captionMargin)
                x = right + captionMargin;
        }
        return x;
    }

    static void PlaceDimensionsOutside(List<Entity> entities,Entity owner,double extensionGap=0.6)
    {
        var box=owner.bounding;
        foreach(var dim in entities.OfType<LinearDimension>().Where(d=>ReferenceEquals(d.Owner,owner)))
        {
            var st=dim.style;st.DimensionLineGap=1.3;st.ArrowSize=1.6;st.ExtensionLineExtend=1.2;
            var middle = (dim.firstReferencePoint + dim.secondReferencePoint) * .5;
            if(Math.Abs(Math.Cos(dim.rotation))<1e-3)
            {
                double axis = owner switch
                {
                    lcdb.Optic.OpticalLens { ShowOpticalAxis: true } single => Math.Max(0, single.AxisPadding),
                    lcdb.Optic.CementedLens { ShowOpticalAxis: true } glue => Math.Max(0, glue.AxisPadding),
                    _ => 0
                };
                double minX = box.right + Math.Max(12, axis + dim.EffectiveTextHeight + st.DimensionLineGap + 1.5);
                double x = ClearSurfaceMarkText(entities, owner, minX);
                x = ClearRadiusCaptions(entities, owner, dim, x);
                dim.SetDimensionLinePosition(new Vector2(x, middle.Y));
                st.ExtensionLineOffset = Math.Max(0, extensionGap);
                dim.Update();
                continue;
            }
            bool upper=dim.userText.StartsWith("(",StringComparison.Ordinal);double level=dim.userText.StartsWith("T2 ",StringComparison.Ordinal)?28:dim.userText.StartsWith("T1 ",StringComparison.Ordinal)?18:8;
            double y=upper?box.top+8:box.bottom-level;
            // Orient from the actual rim (including its mechanical flange) to the desired paper side.
            dim.SetDimensionLinePosition(new Vector2(middle.X, y));
            st.ExtensionLineOffset = Math.Max(0, extensionGap);
            dim.ShortSpanTextOnRight = dim.DrivingParameterKey == "T2";
            dim.ExtendDimensionLineToText = true;
            dim.Update();
        }
    }
}
