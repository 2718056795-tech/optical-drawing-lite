using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using LitMath;
using OtoCAD.Avalonia.Commands;
namespace OtoCAD.Avalonia.Templating;

// Generate the part view using an independent copy of document 1's existing frame.
// Keep its columns, row order, manufacturing text, notes, typography and paper size.
public static class Document1Sheet
{
    public static List<Entity>? Build(Entity part,DrawingFrame template)
    {
        if(part is not OpticalLens && part is not CementedLens)return null;
        var frame=(DrawingFrame)template.Clone();
        var cols=Columns(frame);if(cols is null||cols.Count<3)return null;
        var single=part as OpticalLens;var doublet=part as CementedLens;
        var front=single?.FrontSurface??doublet!.FrontSurface;
        var back=single?.BackSurface??doublet!.BackSurface;
        for(int c=0;c<cols.Count;c++)
        {
            var title=cols[c].Title.ToUpperInvariant();
            bool material=title.Contains("MATERIAL")||title.Contains("材料");
            if(!material&&c==1&&cols.Count==3)material=true;
            bool contact=title.Contains("CEMENT")||title.Contains("胶合");
            bool rear=title.Contains("REAR")||title.Contains("BACK")||title.Contains("右")||title.Contains("后")||c==cols.Count-1;
            var surface=contact&&doublet is not null?doublet.ContactSurface:rear?back:front;
            bool frontSide=!rear&&!contact;
            for(int r=0;r<cols[c].Rows.Count;r++)
            {
                string old=cols[c].Rows[r];
                if(material)cols[c].Rows[r]=MaterialRow(old,r,single,doublet);
                else if(Regex.IsMatch(old,@"^\s*(?:曲率半径\s*[:：]?\s*)?R\s*=?",RegexOptions.IgnoreCase))
                    cols[c].Rows[r]=RadiusRow(old,surface.Radius,frontSide);
                else if(Regex.IsMatch(old,@"^\s*(?:[ØΦφ]e|通光口径)",RegexOptions.IgnoreCase))
                    cols[c].Rows[r]=ReplaceFirstNumber(old,surface.SemiAperture>0?2*surface.SemiAperture:null);
            }
        }
        double border=frame switch {IsoLensDrawingFrame f=>f.BorderMargin,GbLensDrawingFrame f=>f.BorderMargin,DataDrivenFrame f=>f.Definition.Border.Margin,_=>8};
        int rows=cols.Max(c=>c.Rows.Count);
        double block=frame switch
        {
            IsoLensDrawingFrame f=>f.NotesRowHeight+f.TitleRowHeight+(rows+1)*f.TableRowHeight,
            GbLensDrawingFrame f=>f.TitleBlockHeightGb+(rows+1)*f.TableRowHeight+Math.Max(16,f.NoteLines.Count*4.2+4),
            DataDrivenFrame f=>f.BottomBlockHeight(),_=>76
        };
        double diameter=single?.Diameter??doublet!.Diameter;
        double outer=single?.MechanicalDiameter??doublet?.MechanicalDiameter??diameter;
        double thickness=single?.Thickness??(doublet!.T1+doublet.T2);
        double availableH=frame.PaperHeight-2*border-block-40;
        double availableW=frame.PaperWidth-2*border-75;
        double maxK=Math.Min(availableH/Math.Max(1,outer),availableW/Math.Max(1,thickness));
        if(maxK<=0)throw new InvalidOperationException("文档1的表格占满了纸张，请先缩小表格或改用较大纸张。");
        double k=new[]{20.0,10,5,2,1,.5,.2,.1,.05,.02,.01}.FirstOrDefault(v=>v<=maxK);
        if(k<=0)k=maxK;
        string name=single is not null?$"{single.MaterialName} Lens":$"{doublet!.Material1}+{doublet.Material2} Doublet";
        if(frame is IsoLensDrawingFrame iso){iso.ProjectPart=name;iso.DrawScale=LayoutEngine.Label(k);}
        if(frame is GbLensDrawingFrame gb){gb.PartName=name;gb.ScaleText=LayoutEngine.Label(k);}
        frame.InvalidateRenderCache();
        var entities=new List<Entity>{frame};
        double cx=frame.Origin.X+frame.PaperWidth/2;
        double cy=frame.Origin.Y+(border+block+frame.PaperHeight-border)/2;
        Entity view=single is not null?IsoSingleLensSheet.AppendScaledLensView(entities,(OpticalLens)single.Clone(),cx,cy,k):IsoSingleLensSheet.AppendScaledDoubletView(entities,doublet!,cx,cy,k);
        var cursor=new Vector2(frame.Origin.X+border+2,frame.Origin.Y+frame.PaperHeight-border-2);
        IsoSingleLensSheet.EmitAsphericBlock(entities,front,diameter/2,"1",ref cursor,view);
        if(doublet is not null)IsoSingleLensSheet.EmitAsphericBlock(entities,doublet.ContactSurface,diameter/2,"2",ref cursor,view);
        IsoSingleLensSheet.EmitAsphericBlock(entities,back,diameter/2,doublet is null?"2":"3",ref cursor,view);
        return entities;
    }
    public static List<IsoSpecColumn>? Columns(DrawingFrame frame)=>frame switch
    {IsoLensDrawingFrame f=>f.Columns,GbLensDrawingFrame f=>f.Columns,DataDrivenFrame f=>f.Columns,_=>null};
    private static string RadiusRow(string old,double radius,bool front)
    {
        var m=Regex.Match(old,@"(?<=R)\s*=?\s*(?<v>[+-]?(?:\d+(?:\.\d+)?|∞))",RegexOptions.IgnoreCase);
        if(!m.Success)return old;
        var g=m.Groups["v"];string v;
        if(double.IsInfinity(radius))v="∞";
        else
        {
            int digits=g.Value.Contains('.')?g.Value.Length-g.Value.IndexOf('.')-1:3;
            bool signed=g.Value.StartsWith("+")||g.Value.StartsWith("-");
            double value=signed?radius:Math.Abs(radius);
            v=value.ToString("F"+digits,CultureInfo.InvariantCulture);
            if(signed&&radius>=0)v="+"+v;
        }
        string updated=old.Substring(0,g.Index)+v+old.Substring(g.Index+g.Length);
        string shape=(front?radius>0:radius<0)?"CX":"CC";
        if(double.IsInfinity(radius))updated=Regex.Replace(updated,@"\s*(CX|CC)","");
        else if(Regex.IsMatch(updated,"CX|CC"))updated=Regex.Replace(updated,"CX|CC",shape);
        else updated+=" "+shape;
        return updated;
    }
    private static string ReplaceFirstNumber(string text,double? value)
    {
        var m=Regex.Match(text,@"[+-]?\d+(?:\.\d+)?");if(!m.Success)return text;
        int digits=m.Value.Contains('.')?m.Value.Length-m.Value.IndexOf('.')-1:2;
        string replacement=value.HasValue&&double.IsFinite(value.Value)?value.Value.ToString("F"+digits,CultureInfo.InvariantCulture):"待填写";
        return text.Substring(0,m.Index)+replacement+text.Substring(m.Index+m.Length);
    }
    private static string MaterialRow(string old,int row,OpticalLens? lens,CementedLens? d)
    {
        bool nd=Regex.IsMatch(old,@"^\s*n_?d\b",RegexOptions.IgnoreCase);
        bool vd=Regex.IsMatch(old,@"^\s*v_?d\b",RegexOptions.IgnoreCase);
        string n1=lens?.MaterialName??d!.Material1;
        string n2=d?.Material2??"";
        bool known1=GlassLibrary.Find(n1) is not null;
        bool known2=d is null||GlassLibrary.Find(n2) is not null;
        if(nd||vd)
        {
            double a=nd?(lens?.RefractiveIndex??d!.Nd1):(lens?.AbbeNumber??d!.Vd1);
            double b=d is null?0:nd?d.Nd2:d.Vd2;
            string text=ReplaceFirstNumber(old,known1||(nd&&n1.StartsWith("n="))?a:null);
            if(d is not null)text+=" / "+(known2?b.ToString(nd?"F4":"F2",CultureInfo.InvariantCulture):"待填写");
            return text;
        }
        if(row==0)
        {
            var prefix=Regex.Match(old,@"^\s*材料\s*[:：]\s*").Value;
            return prefix+n1+(d is null?"":" / "+n2);
        }
        return old;
    }
}
