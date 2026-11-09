using System;
using System.Collections.Generic;
using System.Linq;
using lcdb;
using lcdb.DrawingFrame;
using LitMath;
namespace OtoCAD.Avalonia.Slim;

public static partial class TemplatePipeline
{
    static IsoSpecColumn CopyColumn(IsoSpecColumn c)=>new(){Title=c.Title,Rows=new(c.Rows),RowKeys=new(c.RowKeys)};
    static IsoSpecColumn ReplaceColumn(IsoSpecColumn c,params (string From,string To)[] map)
    {
        var copy=CopyColumn(c);for(int r=0;r<copy.Rows.Count;r++)foreach(var (from,to) in map)copy.Rows[r]=copy.Rows[r].Replace(Token(from),Token(to),StringComparison.Ordinal);return copy;
    }
    static string DefaultTitle(string current,string original,string replacement)=>current==original?replacement:current;
    static void SetRequirement(IsoSpecColumn c,string original,string text)
    {
        int i=c.Rows.FindIndex(r=>r.Trim()==original.Trim());if(i>=0)c.Rows[i]=text;
    }
    static List<IsoSpecColumn> DoubletColumns(List<IsoSpecColumn> c)
    {
        var front=CopyColumn(c[0]);front.Title=DefaultTitle(front.Title,"左表面","表面1");
        var mat1=ReplaceColumn(c[1],("材料","材料1"),("折射率","折射率1"),("阿贝数","阿贝数1"));
        var back1=ReplaceColumn(c[2],("后表面半径","胶合面片1半径"),("后表面通光口径","胶合面通光口径"));back1.Title=DefaultTitle(back1.Title,"右表面","表面2");SetRequirement(back1,"4/ 90″","4/ —");
        var front2=ReplaceColumn(c[0],("前表面半径","胶合面片2半径"),("前表面通光口径","胶合面通光口径"));front2.Title=DefaultTitle(front2.Title,"左表面","表面3");SetRequirement(front2,"倒角：  0.15","倒角：  —");SetRequirement(front2,"4/ 90″","4/ —");
        var mat2=ReplaceColumn(c[1],("材料","材料2"),("折射率","折射率2"),("阿贝数","阿贝数2"));
        var back=CopyColumn(c[2]);back.Title=DefaultTitle(back.Title,"右表面","表面4");
        return new(){front,mat1,back1,front2,mat2,back};
    }
    static List<IsoSpecColumn> SingleColumns(List<IsoSpecColumn> c)
    {
        var front=CopyColumn(c[0]);front.Title=DefaultTitle(front.Title,"表面1","左表面");
        var material=ReplaceColumn(c[1],("材料1","材料"),("折射率1","折射率"),("阿贝数1","阿贝数"));
        var back=CopyColumn(c[5]);back.Title=DefaultTitle(back.Title,"表面4","右表面");return new(){front,material,back};
    }
    static double GroupHeight(List<IsoSpecColumn> columns,int bands,double rowHeight)
    {
        int per=columns.Count/bands;double h=0;for(int b=0;b<bands;b++)h+=(columns.Skip(b*per).Take(per).Max(c=>c.Rows.Count)+1)*rowHeight;return h;
    }
    public static List<Entity> AdaptReferenceTemplate(IReadOnlyList<Entity> template,bool cemented)
    {
        var copy=template.Select(e=>(Entity)e.Clone()).ToList();double minimumViewY=double.NegativeInfinity;
        foreach(var e in copy)
        {
            if(e is not IsoLensDrawingFrame&&e is not GbLensDrawingFrame)continue;
            var frame=(DrawingFrame)e;var columns=e is IsoLensDrawingFrame iso?iso.Columns:((GbLensDrawingFrame)e).Columns;
            int bands=e is IsoLensDrawingFrame ix?ix.SpecificationBands:((GbLensDrawingFrame)e).SpecificationBands;
            bool adapted=false;
            if(cemented&&bands==1&&columns.Count==3){var expanded=DoubletColumns(columns);columns.Clear();columns.AddRange(expanded);bands=2;adapted=true;}
            else if(!cemented&&bands==2&&columns.Count==6){var single=SingleColumns(columns);columns.Clear();columns.AddRange(single);bands=1;adapted=true;}
            if(e is IsoLensDrawingFrame i)
            {
                i.SpecificationBands=bands;
                if(cemented&&bands==2)minimumViewY=Math.Max(minimumViewY,frame.Origin.Y+i.BorderMargin+i.NotesRowHeight+i.TitleRowHeight+GroupHeight(columns,bands,i.TableRowHeight)+12);
            }
            else if(e is GbLensDrawingFrame g)
            {
                g.SpecificationBands=bands;
                if(cemented&&bands==2)minimumViewY=Math.Max(minimumViewY,frame.Origin.Y+g.BorderMargin+g.TitleBlockHeightGb+GroupHeight(columns,bands,g.TableRowHeight)+Math.Max(16,g.NoteLines.Count*4.2+4)+12);
            }
            if(adapted)e.InvalidateRenderCache();
        }
        foreach(var region in copy.OfType<TemplateViewRegion>())
        {
            if(region.Origin.Y>=minimumViewY)continue;
            double top=region.Origin.Y+region.PaperHeight,height=top-minimumViewY;
            if(height<55)throw new InvalidOperationException("胶合参数表占用空间较大，请缩小表格行高、扩大纸张，或把零件图区移到表格上方。");
            region.Origin=new Vector2(region.Origin.X,minimumViewY);region.PaperHeight=height;region.InvalidateRenderCache();
        }
        return copy;
    }
}
