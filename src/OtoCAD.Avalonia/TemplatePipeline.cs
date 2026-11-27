using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using LitMath;
using OtoCAD.Avalonia.Commands;
using OtoCAD.Avalonia.Templating;
namespace OtoCAD.Avalonia.Slim;

public static partial class TemplatePipeline
{
    public static readonly string[] Fields={"零件名称","图号","材料","材料1","材料2","前表面半径","后表面半径","胶合面半径","中心厚度","片1厚度","片2厚度","通光口径","机械外径","前表面通光口径","后表面通光口径","折射率","阿贝数","折射率1","阿贝数1","折射率2","阿贝数2","胶合面片1半径","胶合面片2半径","胶合面通光口径","比例","日期","源文件","原始元件号","源面号","源配置"};
    public static string Token(string name)=>"【"+name+"】";
    static string F(double v)=>double.IsInfinity(v)?"∞":double.IsFinite(v)?v.ToString("0.######",CultureInfo.InvariantCulture):"待填写";
    static string R(double v,bool front)=>double.IsInfinity(v)?"R∞":"R"+Math.Abs(v).ToString("F3",CultureInfo.InvariantCulture)+"/"+((front?v>0:v<0)?"CX":"CC");
    internal static double OuterDiameter(Entity part) => part switch
    {
        OpticalLens l => OpticSurfaceGeometry.OuterDiameter(l.Diameter, l.MechanicalDiameter, l.FrontSurface, l.BackSurface),
        CementedLens d => OpticSurfaceGeometry.OuterDiameter(d.Diameter, d.MechanicalDiameter, d.FrontSurface, d.ContactSurface, d.BackSurface),
        _ => throw new NotSupportedException("仅支持单透镜和双胶合透镜。")
    };

    public static bool IsIllustrative(Entity part)
    {
        var ext=(part as OpticalLens)?.Extensions??(part as CementedLens)?.Extensions;return ext?.Tags.TryGetValue("apertureSource",out var value)==true&&value=="illustrative";
    }
    public static bool TrySetIllustrativeAperture(Entity part)
    {
        var l=part as OpticalLens;var d=part as CementedLens;if(l is null&&d is null)return false;
        var surfaces=l is not null?new[]{l.FrontSurface,l.BackSurface}:new[]{d!.FrontSurface,d.ContactSurface,d.BackSurface};if(surfaces.Any(s=>s.SemiAperture>0))return false;
        double original=l?.Diameter??d!.Diameter;double? mech=l?.MechanicalDiameter??d?.MechanicalDiameter;
        foreach(double candidate in new[]{10.0,8,6,5,4,3,2,1,.5,.2,.1})
        {
            double v=mech.HasValue?Math.Min(mech.Value,candidate):candidate;if(l is not null)l.Diameter=v;else d!.Diameter=v;foreach(var surf in surfaces)surf.SemiAperture=v/2;
            try
            {
                ValidatePart(part);var ext=l is not null?(l.Extensions??=new OpticalExtensions()):(d!.Extensions??=new OpticalExtensions());ext.Tags["apertureSource"]="illustrative";ext.Tags["apertureNote"]="文件未提供口径，选用不相交的示意尺寸";return true;
            }
            catch(InvalidOperationException){}
        }
        if(l is not null)l.Diameter=original;else d!.Diameter=original;foreach(var surf in surfaces)surf.SemiAperture=0;return false;
    }
    public static Dictionary<string,string> Values(Entity part,string name,string number,string source,string scale,string? drawingDate=null)
    {
        var l=part as OpticalLens;var d=part as CementedLens;
        if(l is null&&d is null)throw new NotSupportedException("当前支持单透镜与双胶合透镜。");
        var f=l?.FrontSurface??d!.FrontSurface;var b=l?.BackSurface??d!.BackSurface;
        string m=l?.MaterialName??d!.Material1;
        bool known=double.IsFinite(l?.RefractiveIndex??d!.Nd1)&&(l?.RefractiveIndex??d!.Nd1)>1;
        bool known2=d is null||double.IsFinite(d.Nd2)&&d.Nd2>1;
        bool knownV=double.IsFinite(l?.AbbeNumber??d!.Vd1)&&(l?.AbbeNumber??d!.Vd1)>0;
        bool knownV2=d is null||double.IsFinite(d.Vd2)&&d.Vd2>0;
        return new(){["零件名称"]=name,["图号"]=number,["材料"]=l is not null?m:d!.Material1+" / "+d.Material2,["材料1"]=m,["材料2"]=d?.Material2??"—",
          ["前表面半径"]=R(f.Radius,true),["后表面半径"]=R(b.Radius,false),["胶合面半径"]=d is null?"—":R(d.RContact,false),
          ["中心厚度"]=F(l?.Thickness??d!.T1+d.T2),["片1厚度"]=F(l?.Thickness??d!.T1),["片2厚度"]=d is null?"—":F(d.T2),
          ["通光口径"]=F(l?.Diameter??d!.Diameter),["机械外径"]=F(OuterDiameter(part)),["前表面通光口径"]=f.SemiAperture>0?F(f.SemiAperture*2)+(IsIllustrative(part)?"（示意）":""):"待填写",["后表面通光口径"]=b.SemiAperture>0?F(b.SemiAperture*2)+(IsIllustrative(part)?"（示意）":""):"待填写",
          ["折射率"]=(known?(l?.RefractiveIndex??d!.Nd1).ToString("F3",CultureInfo.InvariantCulture):"待填写")+(d is null?"":" / "+(known2?d.Nd2.ToString("F3",CultureInfo.InvariantCulture):"待填写")),
          ["阿贝数"]=(knownV?(l?.AbbeNumber??d!.Vd1).ToString("F3",CultureInfo.InvariantCulture):"待填写")+(d is null?"":" / "+(knownV2?d.Vd2.ToString("F3",CultureInfo.InvariantCulture):"待填写")),
          ["折射率1"]=known?(l?.RefractiveIndex??d!.Nd1).ToString("F3",CultureInfo.InvariantCulture):"待填写",
          ["阿贝数1"]=knownV?(l?.AbbeNumber??d!.Vd1).ToString("F3",CultureInfo.InvariantCulture):"待填写",
          ["折射率2"]=d is null?"—":known2?d.Nd2.ToString("F3",CultureInfo.InvariantCulture):"待填写",
          ["阿贝数2"]=d is null?"—":knownV2?d.Vd2.ToString("F3",CultureInfo.InvariantCulture):"待填写",
          ["胶合面片1半径"]=d is null?"—":R(d.RContact,false),["胶合面片2半径"]=d is null?"—":R(d.RContact,true),
          ["胶合面通光口径"]=d is null?"—":d.ContactSurface.SemiAperture>0?F(d.ContactSurface.SemiAperture*2)+(IsIllustrative(part)?"（示意）":""):"待填写",
          ["比例"]=scale,["日期"]=drawingDate??DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),["源文件"]=source,
          ["原始元件号"]=lcdb.Optic.Import.ImportIdentity.Tag(part,"sourceElementNumber"),
          ["源面号"]=lcdb.Optic.Import.ImportIdentity.Tag(part,"sourceSurfaces"),
          ["源配置"]=lcdb.Optic.Import.ImportIdentity.Tag(part,"sourceConfiguration") is { Length: > 0 } z ? "Z"+z : "—"};
    }
    public static bool TableIncludesParameter(IReadOnlyList<Entity> template,params string[] fields)
    {
        foreach(var entity in template)
        {
            var columns=entity is TemplateTable table?table.Columns:entity is DrawingFrame frame?Document1Sheet.Columns(frame):null;
            if(columns is not null&&columns.SelectMany(c=>c.Rows).Any(row=>fields.Any(field=>row.Contains(Token(field),StringComparison.Ordinal))))return true;
        }
        return false;
    }
    public static List<Entity> Build(IReadOnlyList<Entity> template,Entity part,string name,string number,string source)
    {
        ValidatePart(part);
        if (IsGeneratedDrawing(template)) template = RestoreReusableTemplate(template);
        template=AdaptReferenceTemplate(template,part is CementedLens);
        var result=new List<Entity>();
        ExpandAutoRegions(template);
        var regions=template.OfType<TemplateViewRegion>().ToList();
        var frame=template.OfType<DrawingFrame>().FirstOrDefault(f=>f is not TemplateViewRegion&&f is not TemplateTable);
        if(regions.Count==0)
        {
            double pw=frame?.PaperWidth??210,ph=frame?.PaperHeight??297;
            regions.Add(new TemplateViewRegion{Origin=new Vector2(30,ph*.38),PaperWidth=pw-60,PaperHeight=ph*.55});
        }
        var l=part as OpticalLens;var d=part as CementedLens;
        double diameter=l?.Diameter??d?.Diameter??25;
        double outer=OuterDiameter(part);
        double thick=l?.Thickness??(d?.T1??0)+(d?.T2??0);
        var surfaces=new List<(OpticalSurface Surface,string Label)>{(l?.FrontSurface??d!.FrontSurface,"1"),(l?.BackSurface??d!.BackSurface,d is null?"2":"3")};
        if(d is not null)surfaces.Insert(1,(d.ContactSurface,"2"));
        double scaleLabelHeight = regions.Count > 1 ? 6 : 0;
        double reserved=(IsIllustrative(part)?7:0) + scaleLabelHeight;
        foreach(var (surface,label) in surfaces) if(surface is AsphericSurface a){var block=lcdb.Annotation.AsphericDataBlock.FromSurface(a,diameter/2,label,new Vector2(0,0));reserved+=block.bounding.height+10;}
        bool frontRadiusOnDrawing=!TableIncludesParameter(template,"前表面半径"),backRadiusOnDrawing=!TableIncludesParameter(template,"后表面半径");
        bool contactRadiusOnDrawing=!TableIncludesParameter(template,"胶合面半径","胶合面片1半径","胶合面片2半径");
        double leftPad=frontRadiusOnDrawing||contactRadiusOnDrawing&&d is not null?52:26,rightPad=backRadiusOnDrawing?50:30;
        double bottomPad=d is null?14:34,topPad=16;
        double bodyWidth=Math.Max(.001,part.bounding.width);
        var scales=new List<double>();
        foreach(var r in regions)
        {
            double max=Math.Min((r.PaperWidth-leftPad-rightPad)/bodyWidth,(r.PaperHeight-reserved-topPad-bottomPad)/Math.Max(.001,outer));
            if (!double.IsFinite(r.ViewScale) || r.ViewScale < 0 || !double.IsFinite(max) || max <= 0)
                throw new InvalidOperationException($"第 {scales.Count + 1} 个零件图区的尺寸或比例无效。");
            if (r.ViewScale > max + 1e-9)
                throw new InvalidOperationException($"第 {scales.Count + 1} 个零件图区容不下固定比例 {LayoutEngine.Label(r.ViewScale)} 的镜片与标注。请降低比例、扩大图区或设为自动。");
            double k=r.ViewScale>0?r.ViewScale:max>=1?Math.Floor(max*.985*10)/10:1/Math.Ceiling(1/(max*.985));
            if(!double.IsFinite(k)||k<=0)throw new InvalidOperationException("零件图区太小，请放大图区或调整比例。");scales.Add(k);
        }
        var values=Values(part,name,number,source,SheetScaleLabel(scales));
        foreach(var original in template)
        {
            if(original is TemplateViewRegion||original is OpticalLens||original is CementedLens||original.Owner is OpticalLens||original.Owner is CementedLens)continue;
            Entity copy=(Entity)original.Clone();
            BindTracked(copy,values);result.Add(copy);
        }
        for(int i=0;i<regions.Count;i++)
        {
            int generatedStart=result.Count;
            var r=regions[i];double cx=r.Origin.X+leftPad+(r.PaperWidth-leftPad-rightPad)/2-(part.bounding.center.X-(l?.Position.X??d!.Position.X))*scales[i],cy=r.Origin.Y+bottomPad+(r.PaperHeight-reserved-topPad-bottomPad)/2;
            Entity owner=l is not null?IsoSingleLensSheet.AppendScaledLensView(result,(OpticalLens)l.Clone(),cx,cy,scales[i],frontRadiusOnDrawing,backRadiusOnDrawing):IsoSingleLensSheet.AppendScaledDoubletView(result,d!,cx,cy,scales[i],frontRadiusOnDrawing,contactRadiusOnDrawing,backRadiusOnDrawing);
            owner.DrawingBinding=new OpticalDrawingBinding{ViewScale=scales[i],FixedBodyWidth=r.ViewScale>0?r.PaperWidth-leftPad-rightPad:0,
                FixedBodyHeight=r.ViewScale>0?r.PaperHeight-reserved-topPad-bottomPad:0,DrawingDate=values["日期"],Name=name,Number=number,Source=source,Regions=regions.Select(x=>(TemplateViewRegion)x.Clone()).ToList(),LastMaterial1=l?.MaterialName??d!.Material1,LastMaterial2=d?.Material2??""};
            foreach(var dimension in result.OfType<LinearDimension>().Where(x=>ReferenceEquals(x.Owner,owner)))dimension.ConfigureAutomaticMeasurement(scales[i]);
            PlaceDimensionsOutside(result,owner,r.DimensionExtensionGap);
            if (regions.Count > 1)
                result.Add(new Text("视图 " + (i+1) + "  " + LayoutEngine.Label(scales[i]), new Vector3(r.Origin.X+2,r.Origin.Y+r.PaperHeight-4,0),2.2));
            if(IsIllustrative(part))result.Add(new Text("口径为示意值（文件未给定，可修改）",new Vector3(r.Origin.X+2,r.Origin.Y+r.PaperHeight-4-scaleLabelHeight,0),2.2));
            var cursor=new Vector2(r.Origin.X+2,r.Origin.Y+r.PaperHeight-scaleLabelHeight-(IsIllustrative(part)?9:2));
            IsoSingleLensSheet.EmitAsphericBlock(result,l?.FrontSurface??d!.FrontSurface,diameter/2,"1",ref cursor,owner);
            if(d is not null)IsoSingleLensSheet.EmitAsphericBlock(result,d.ContactSurface,diameter/2,"2",ref cursor,owner);
            IsoSingleLensSheet.EmitAsphericBlock(result,l?.BackSurface??d!.BackSurface,diameter/2,d is null?"2":"3",ref cursor,owner);
            foreach(var annotation in result.Skip(generatedStart).Where(x=>!ReferenceEquals(x,owner))){annotation.Source=EntitySource.OpticalElement;annotation.IsGenerated=true;}
            owner.DrawingBinding.LastGeometrySignature=GeometrySignature(owner);
            ValidateViewPlacement(owner,frame);
        }
        return result;
    }
    public static void ValidatePart(Entity part)
    {
        var l=part as OpticalLens;var d=part as CementedLens;
        if(l is null&&d is null)throw new InvalidOperationException("仅支持单透镜和双胶合透镜。");
        double diameter=l?.Diameter??d!.Diameter,thickness=l?.Thickness??d!.T1+d.T2;
        double? mechanical=l?.MechanicalDiameter??d?.MechanicalDiameter;
        if(mechanical.HasValue&&(!double.IsFinite(mechanical.Value)||mechanical.Value<=0||mechanical.Value<diameter-1e-8))throw new InvalidOperationException("机械外径不能小于通光口径，且必须为正数。");
        if(!double.IsFinite(diameter)||diameter<=0||!double.IsFinite(thickness)||thickness<=0)throw new InvalidOperationException("口径或中心厚度无效，请检查设计数据。");
        var front=l?.FrontSurface??d!.FrontSurface;var back=l?.BackSurface??d!.BackSurface;
        var surfaces=d is null?new[]{front,back}:new[]{front,d.ContactSurface,back};
        if(d is not null&&(!double.IsFinite(d.T1)||!double.IsFinite(d.T2)||d.T1<=0||d.T2<=0))throw new InvalidOperationException("胶合透镜两片中心厚度必须分别为正数。");
        foreach(var surface in surfaces){
            if(!double.IsFinite(surface.SemiAperture)||surface.SemiAperture<0)throw new InvalidOperationException("逐面通光半口径必须为非负有限数值（0表示未单独指定）。");
            if (mechanical.HasValue && surface.CurveSemiAperture * 2 > mechanical.Value + 1e-8)
                throw new InvalidOperationException("机械外径小于已有曲面延伸区外径，请核对外径与延伸区。");
            if(surface.SemiAperture*2>diameter+1e-8)throw new InvalidOperationException("整体通光口径小于已有逐面通光口径。请保留不小于各面的整体口径，或先修改对应表面的口径。");
            if(double.IsNaN(surface.Radius)||surface.Radius==0)throw new InvalidOperationException("表面曲率半径无效；平面请使用无穷大半径。");
            if(surface is AsphericSurface a&&(!double.IsFinite(a.ConicConstant)||!double.IsFinite(a.QuadraticCoefficient)||a.EvenCoefficients is null||a.EvenCoefficients.Any(x=>!double.IsFinite(x))))throw new InvalidOperationException("非球面系数必须为有限数值。");
        }
        if (d is null)
        {
            SurfaceClearance.ValidatePositiveThickness(front, back, thickness, diameter / 2);
        }
        else
        {
            SurfaceClearance.ValidatePositiveThickness(front, d.ContactSurface, d.T1, diameter / 2, "胶合片1");
            SurfaceClearance.ValidatePositiveThickness(d.ContactSurface, back, d.T2, diameter / 2, "胶合片2");
        }
    }
    static string S(string text,Dictionary<string,string> v){foreach(var x in v)text=text.Replace(Token(x.Key),x.Value,StringComparison.Ordinal);return text;}
    static void C(List<IsoSpecColumn> cols,Dictionary<string,string> v){foreach(var c in cols){c.Title=S(c.Title,v);for(int i=0;i<c.Rows.Count;i++)c.Rows[i]=S(c.Rows[i],v);}}
    static void Bind(Entity e,Dictionary<string,string> v)
    {
        if(e is Text t)t.Value=S(t.Value,v);
        if(e is MText mt)mt.Value=S(mt.Value,v);
        if(e is TemplateTable table)C(table.Columns,v);
        if(e is IsoLensDrawingFrame iso){C(iso.Columns,v);iso.Notes=S(iso.Notes,v);iso.GeneralTol=S(iso.GeneralTol,v);iso.ProjectPart=S(iso.ProjectPart,v);iso.DrawnBy=S(iso.DrawnBy,v);iso.DrawDate=S(iso.DrawDate,v);iso.DrawScale=v["比例"];}
        if(e is GbLensDrawingFrame gb){C(gb.Columns,v);gb.NoteLines=gb.NoteLines.Select(s=>S(s,v)).ToList();gb.PartName=S(gb.PartName,v);gb.DrawingNumberGb=S(gb.DrawingNumberGb,v);gb.ScaleText=v["比例"];}
        if(e is DataDrivenFrame df){C(df.Columns,v);foreach(var key in df.FieldValues.Keys.ToArray())df.FieldValues[key]=S(df.FieldValues[key],v);}
        e.InvalidateRenderCache();
    }
    internal static List<Entity> OriginalPreset(string key)
    {
        var definitionPath = Path.Combine(AppContext.BaseDirectory, "Config", "Frames", key + ".json");
        var frame = new DataDrivenFrame(FrameDefinition.Load(definitionPath));
        frame.EnsureColumns();
        foreach (var column in frame.Columns)
        {
            for (int i = 0; i < column.RowKeys.Count; i++)
            {
                string value = column.Rows[i];
                string separator = value.Contains(" = ", StringComparison.Ordinal) ? " = " : " ";
                column.Rows[i] = column.RowKeys[i] switch
                {
                    "radius_front" => Token("前表面半径"),
                    "radius_back" => Token("后表面半径"),
                    "clear_aperture_front" => "Øe " + Token("前表面通光口径"),
                    "clear_aperture_back" => "Øe " + Token("后表面通光口径"),
                    "material_name" => Token("材料"),
                    "n_d" => "n_d" + separator + Token("折射率"),
                    "v_d" => "v_d" + separator + Token("阿贝数"),
                    _ => value,
                };
            }
        }
        foreach (var field in frame.Definition.NamedFields())
        {
            string? token = field.Key switch
            {
                "ProjectPart" or "name" => "零件名称",
                "product_number" => "图号",
                "DrawDate" => "日期",
                "DrawScale" or "scale" => "比例",
                _ => null,
            };
            if (token is not null) frame.FieldValues[field.Key] = Token(token);
        }
        double bottom = Math.Max(108, frame.Definition.Border.Margin + frame.BottomBlockHeight() + 12);
        return new List<Entity>
        {
            frame,
            new TemplateViewRegion
            {
                Origin = new Vector2(35, bottom), PaperWidth = frame.PaperWidth - 70,
                PaperHeight = frame.PaperHeight - 19 - bottom,
            },
        };
    }

    public static List<Entity> Preset(string kind)
    {
        DrawingFrame frame;
        string notes="1. 左右表面均镀增透膜且镀膜范围大于通光口径 Φe\n2. 膜层反射率 RAVG(@420nm to 680nm, 0° to 25°)<0.5%\n3. 镜片外径及平台面涂墨，尺寸为涂墨后尺寸，墨水型号：GT-7II";
        if(kind=="GB")
        {
            var gb=GbSheetScene.BuildSample().OfType<GbLensDrawingFrame>().Single();
            gb.PartName=Token("零件名称");gb.DrawingNumberGb=Token("图号");
            gb.NoteLines=notes.Split('\n').ToList();frame=gb;
        }
        else if(kind=="Blank")frame=new DrawingFrame{PaperWidth=210,PaperHeight=297,ShowTitleBlock=false};
        else
        {
            var iso=IsoSingleLensSheet.BuildSample().OfType<IsoLensDrawingFrame>().Single();
            iso.Notes=notes;iso.NotesLabel="技术要求";iso.NotesRowHeight=22;iso.ProjectPart=Token("零件名称");iso.DrawnBy="";iso.DrawDate=Token("日期");frame=iso;
        }
        var cols=Document1Sheet.Columns(frame);
        if(cols is not null&&cols.Count>=3)
        {
            cols[0].Title="左表面";cols[1].Title="材料技术要求";cols[2].Title="右表面";
            cols[0].Rows=new(){"曲率半径　"+Token("前表面半径"),"Φe　"+Token("前表面通光口径"),"倒角：  0.15","3/ 3(0.5)","4/ —","5/ 5×0.16; L2×0.04; E0.5","6/ —"};
            cols[1].Rows=new(){"材料　"+Token("材料"),"n_d　"+Token("折射率")+" ±0.0003","v_d　"+Token("阿贝数")+" ±0.5%","0/ 10","1/ 3×0.25","2/ 4;4"};
            cols[2].Rows=new(){"曲率半径　"+Token("后表面半径"),"Φe　"+Token("后表面通光口径"),"倒角：  0.15","3/ 3(0.5)","4/ 90″","5/ 5×0.16; L2×0.04; E0.5","6/ —"};
        }
        var entities=new List<Entity>{frame,new TemplateViewRegion{Origin=new Vector2(35,108),PaperWidth=140,PaperHeight=170,UseFullPaperArea=kind!="Blank"}};
        return kind=="Glue"?AdaptReferenceTemplate(entities,true):entities;
    }
    private static void ValidateViewPlacement(Entity view, DrawingFrame? paper)
    {
        var bounds = view.bounding;
        const double tolerance = 1e-7;
        if (paper is not null &&
            (bounds.left < paper.Origin.X-tolerance || bounds.right > paper.Origin.X+paper.PaperWidth+tolerance ||
             bounds.bottom < paper.Origin.Y-tolerance || bounds.top > paper.Origin.Y+paper.PaperHeight+tolerance))
            throw new InvalidOperationException("镜片轮廓已超出纸张。请调整镜片位置、图区大小或比例。");

        if (view.DrawingBinding is not {} binding) return;
        double width = binding.FixedBodyWidth ?? 0, height = binding.FixedBodyHeight ?? 0;
        if (width<=0 || height<=0)
        {
            // Zero identifies a new auto-fit view; null identifies a file from before these fields existed.
            // A legacy auto-fit view can coincidentally share a fixed view's scale, so never infer its region.
            if (binding.FixedBodyWidth.HasValue || binding.FixedBodyHeight.HasValue ||
                binding.Regions?.Any(r=>r.ViewScale<=0)==true) return;
            var matches = binding.Regions?.Where(r => r.ViewScale > 0 && r.ViewScale == binding.ViewScale).Take(2).ToList();
            if (matches is null || matches.Count!=1) return;
            width=matches[0].PaperWidth;
            height=matches[0].PaperHeight;
        }
        double outer = OuterDiameter(view);
        if (bounds.width > width+tolerance || Math.Max(bounds.height,outer)>height+tolerance)
            throw new InvalidOperationException($"镜片尺寸已超出固定比例 {LayoutEngine.Label(binding.ViewScale)} 的图区。请重新调整设计尺寸或用较大图区、较低比例重新出图。");
    }

    public static void Export(CadCanvas canvas,string stem,IReadOnlyList<string> formats)
    {
        SynchronizeGeneratedSheet(canvas.GetAllEntities());
        var paper=canvas.GetAllEntities().OfType<DrawingFrame>().FirstOrDefault(f=>f is not TemplateTable&&f is not TemplateViewRegion);
        foreach (var view in canvas.GetAllEntities().Where(e=>e is OpticalLens or CementedLens).Where(canvas.IsEntityVisible))
        {
            if (view.DrawingBinding is not null) ValidatePart(SourcePartFromView(view));
            ValidateViewPlacement(view,paper);
        }
        foreach(string format in formats)
        {
            string file=stem+"."+format;
            if (CadCanvas.SameFile(file, canvas.CurrentFilePath))
                throw new InvalidOperationException("导出目标与当前图纸相同，请选择其他文件名。");
            if(format=="pdf")canvas.ExportPaperPdf(file,paper?.Origin??Vector2.Zero,paper?.PaperWidth??210,paper?.PaperHeight??297);
            else if(format=="png")canvas.ExportPaperPng(file,paper?.Origin??Vector2.Zero,paper?.PaperWidth??210,paper?.PaperHeight??297);
            else if(format=="dxf")canvas.ExportToDxf(file);
            else if(format is "otocad" or "json") canvas.SaveAutoBackup(file);
            else throw new ArgumentException("未知导出格式：" + format, nameof(formats));
        }
    }
}

