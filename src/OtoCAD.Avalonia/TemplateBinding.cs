using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using OtoCAD.Avalonia.Commands;
using OtoCAD.Avalonia.Templating;
namespace OtoCAD.Avalonia.Slim;
public static partial class TemplatePipeline
{
    private sealed record TextSlot(string Key,Func<string> Get,Action<string> Set);
    private static string[] NamedProperties(Entity e) => e switch {
            IsoLensDrawingFrame=>new[]{"Notes","GeneralTol","ProjectPart","DrawnBy","ApprovedBy","DrawDate","DrawScale","Sheet","Rev"},
            GbLensDrawingFrame=>new[]{"Unit","PartName","DrawingNumberGb","Designer","StdCheck","Reviewer","ProcessBy","Approver","Stage","Weight","ScaleText","SheetOf","GeneralRoughness"},
            OpticalDrawingFrame=>new[]{"ProjectName","CompanyName","Material","DrawingNumber","Designer","DesignDate","DrawingTitle","Scale"},
            _=>Array.Empty<string>()};
    private static IEnumerable<TextSlot> TextSlots(Entity e)
    {
        var cols=e is TemplateTable tt?tt.Columns:e is DrawingFrame f?Document1Sheet.Columns(f):null;
        if(cols is not null)for(int c=0;c<cols.Count;c++){
            int ci=c;yield return new($"c:{c}:title",()=>cols[ci].Title,v=>cols[ci].Title=v);
            for(int r=0;r<cols[c].Rows.Count;r++){int ri=r;yield return new($"c:{c}:r:{r}",()=>cols[ci].Rows[ri],v=>cols[ci].Rows[ri]=v);}
        }
        if(e is Text t)yield return new("text",()=>t.Value,v=>t.Value=v);
        if(e is MText m)yield return new("mtext",()=>m.Value,v=>m.Value=v);
        foreach(string name in NamedProperties(e)){var p=e.GetType().GetProperty(name)!;yield return new("p:"+name,()=>(string?)p.GetValue(e)??"",v=>p.SetValue(e,v));}
        if (e is GbLensDrawingFrame gb)
        {
            // Older drawings tracked individual note rows; keep reading these until the entire field is edited.
            if (e.TemplateTextBindings?.Keys.Any(k => k.StartsWith("note:", StringComparison.Ordinal)) == true)
                for (int r = 0; r < gb.NoteLines.Count; r++)
                {
                    int index = r;
                    yield return new("note:" + index, () => gb.NoteLines[index], v => gb.NoteLines[index] = v);
                }
            else yield return new("p:Notes", () => string.Join("\n", gb.NoteLines), v => gb.NoteLines = v.Split('\n').ToList());
        }
        if (e is DataDrivenFrame df)
            foreach (string key in df.FieldValues.Keys.Concat(df.Definition.NamedFields().Select(field => field.Key)).Distinct().ToArray())
                yield return new("field:" + key, () => df.GetField(key), v => df.FieldValues[key] = v);
    }
    private static Dictionary<string,string> ExpressionValues(string expression,Dictionary<string,string> values)
        =>values.Where(v=>expression.Contains(Token(v.Key),StringComparison.Ordinal)).ToDictionary(v=>v.Key,v=>v.Value);
    private static void BindTracked(Entity entity,Dictionary<string,string> values)
    {
        entity.TemplateTextBindings=new();
        foreach(var slot in TextSlots(entity)){
            string expression=slot.Get();
            if(slot.Key=="p:DrawScale"||slot.Key=="p:ScaleText")expression=Token("比例");
            if(!expression.Contains('【'))continue;
            string rendered=S(expression,values);slot.Set(rendered);
            entity.TemplateTextBindings[slot.Key]=new(){Expression=expression,LastRendered=rendered,LastValues=ExpressionValues(expression,values)};
        }
        entity.InvalidateRenderCache();
    }
    // Retain edits to prefixes/suffixes/tolerances while restoring the actual parameter token.
    private static string ReconcileExpression(TemplateTextBinding binding,string current)
    {
        if(current==binding.LastRendered)return binding.Expression;
        var replacements=new List<(int Start,int Length,string Token)>();
        int expressionCursor=0,renderedCursor=0;
        foreach(Match match in Regex.Matches(binding.Expression,"【([^】]+)】")){
            string key=match.Groups[1].Value;
            renderedCursor+=match.Index-expressionCursor;expressionCursor=match.Index+match.Length;
            if(!binding.LastValues.TryGetValue(key,out string? value)||string.IsNullOrEmpty(value))continue;
            int originalStart=renderedCursor;renderedCursor+=value.Length;
            if(current.Contains(match.Value,StringComparison.Ordinal))continue;
            bool numeric=double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out _);
            bool RightBoundary(string text,int at)=>!numeric||at+value.Length>=text.Length||!"0123456789.eE".Contains(text[at+value.Length]);
            var candidates=new List<int>();
            for(int start=0;(start=current.IndexOf(value,start,StringComparison.Ordinal))>=0;start+=value.Length){
                if(!RightBoundary(current,start))continue;
                if(numeric&&start>0&&"0123456789.eE+-".Contains(current[start-1]))continue;
                candidates.Add(start);
            }
            int chosen=-1;
            string originalPrefix=binding.LastRendered[..Math.Min(originalStart,binding.LastRendered.Length)];
            if(current.StartsWith(originalPrefix,StringComparison.Ordinal)){
                if(candidates.Contains(originalPrefix.Length))chosen=originalPrefix.Length;
                else return binding.Expression; // The parameter itself was edited; do not steal a tolerance occurrence.
            }
            if(chosen<0){
                // Use the parameter's ordinal among actual numeric/string occurrences; a digit
                // inside a tolerance such as 0.1 is not the 1 mm design value.
                int ordinal=0;
                for(int at=0;(at=binding.LastRendered.IndexOf(value,at,StringComparison.Ordinal))>=0&&at<originalStart;at+=value.Length){
                    if(!RightBoundary(binding.LastRendered,at))continue;
                    if(numeric&&at>0&&"0123456789.eE+-".Contains(binding.LastRendered[at-1]))continue;
                    ordinal++;
                }
                if(ordinal<candidates.Count)chosen=candidates[ordinal];
            }
            if(chosen<0)return binding.Expression;
            replacements.Add((chosen,value.Length,match.Value));
        }
        string expression=current;int previousStart=current.Length;
        foreach(var replacement in replacements.OrderByDescending(x=>x.Start)){
            if(replacement.Start+replacement.Length>previousStart)return binding.Expression;
            expression=expression[..replacement.Start]+replacement.Token+expression[(replacement.Start+replacement.Length)..];previousStart=replacement.Start;
        }
        // Numerical design values stay bound; only the surrounding prose/tolerances are editable here.
        foreach(Match match in Regex.Matches(binding.Expression,"【[^】]+】"))
            if(!expression.Contains(match.Value,StringComparison.Ordinal))return binding.Expression;
        return expression;
    }
    public static bool IsGeneratedDrawing(IReadOnlyList<Entity> entities)=>entities.Any(e => e.DrawingBinding is not null) || (entities.Any(e => e is lcdb.DrawingFrame.DrawingFrame) && entities.Any(e => e is OpticalLens or CementedLens));
    public static Entity SourcePartFromView(Entity view)
    {
        double k=view.DrawingBinding?.ViewScale??1;if(!double.IsFinite(k)||k<=0)throw new InvalidOperationException("图纸比例无效，不能反算设计尺寸。");
        var part=(Entity)view.Clone();part.DrawingBinding=null;
        void Surface(OpticalSurface surface){if(double.IsFinite(surface.Radius))surface.Radius/=k;surface.SemiAperture/=k;surface.CurveSemiAperture/=k;if(surface is AsphericSurface a){for(int j=0;j<a.EvenCoefficients.Length;j++)a.EvenCoefficients[j]*=Math.Pow(k,2*j+3);a.QuadraticCoefficient*=k;}}
        if(part is OpticalLens l){l.MechanicalDiameter/=k;l.Diameter/=k;l.Thickness/=k;Surface(l.FrontSurface);Surface(l.BackSurface);l.Position=LitMath.Vector2.Zero;}
        else if(part is CementedLens d){d.MechanicalDiameter/=k;d.Diameter/=k;d.T1/=k;d.T2/=k;Surface(d.FrontSurface);Surface(d.ContactSurface);Surface(d.BackSurface);d.Position=LitMath.Vector2.Zero;}
        part.InvalidateRenderCache();return part;
    }
    private static string GeometrySignature(Entity view)
    {
        var l=view as OpticalLens;var d=view as CementedLens;
        var surfaces=l is not null?new[]{l.FrontSurface,l.BackSurface}:new[]{d!.FrontSurface,d.ContactSurface,d.BackSurface};
        string geometry = JsonSerializer.Serialize(new{D=l?.Diameter??d!.Diameter,M=l?.MechanicalDiameter??d?.MechanicalDiameter,T=l?.Thickness??d!.T1,T2=d?.T2,Material=l?.MaterialName??d!.Material1,Material2=d?.Material2,Nd=l?.RefractiveIndex??d!.Nd1,Vd=l?.AbbeNumber??d!.Vd1,Nd2=d?.Nd2,Vd2=d?.Vd2,Surfaces=surfaces.Select(s=>new{s.Radius,s.SemiAperture,K=(s as AsphericSurface)?.ConicConstant,A2=(s as AsphericSurface)?.QuadraticCoefficient,A=(s as AsphericSurface)?.EvenCoefficients})},new JsonSerializerOptions{NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals});
        // Keep existing signatures comparable when opening drawings saved without explicit curve limits.
        var curveRadii = surfaces.Select(s => s.CurveSemiAperture).ToArray();
        return curveRadii.Any(r => r.HasValue) ? geometry + "|" + JsonSerializer.Serialize(curveRadii) : geometry;
    }
    private static string SheetScaleLabel(IEnumerable<double> scales)
    {
        var distinct = scales.Select(LayoutEngine.Label).Distinct().Take(2).ToList();
        return distinct.Count == 1 ? distinct[0] : "见各视图";
    }

    private static string DrawingDate(IReadOnlyList<Entity> entities, OpticalDrawingBinding info)
    {
        foreach (var frame in entities.OfType<IsoLensDrawingFrame>())
        {
            if (frame.TemplateTextBindings?.TryGetValue("p:DrawDate", out var field) == true &&
                field.OverrideText is null && field.Expression == Token("日期") && frame.DrawDate != field.LastRendered)
                return frame.DrawDate;
        }
        if (!string.IsNullOrEmpty(info.DrawingDate)) return info.DrawingDate;
        // Earlier files retain the creation date in the value captured by the template binding.
        foreach (var entity in entities)
            if (entity.TemplateTextBindings is not null)
                foreach (var field in entity.TemplateTextBindings.Values)
                    if (field.LastValues.TryGetValue("日期", out var date)) return date;
        return entities.OfType<IsoLensDrawingFrame>().FirstOrDefault()?.DrawDate ?? "";
    }
    private static void CopyScaledGeometry(Entity source,Entity target)
    {
        double k=target.DrawingBinding!.ViewScale;
        OpticalSurface Surf(OpticalSurface s)
        {
            var copy = s.Clone();
            IsoSingleLensSheet.ScaleSurface(copy, k);
            return copy;
        }
        if(source is OpticalLens l&&target is OpticalLens t){t.Diameter=l.Diameter*k;t.MechanicalDiameter=l.MechanicalDiameter*k;t.Thickness=l.Thickness*k;t.FrontSurface=Surf(l.FrontSurface);t.BackSurface=Surf(l.BackSurface);t.MaterialName=l.MaterialName;t.RefractiveIndex=l.RefractiveIndex;t.AbbeNumber=l.AbbeNumber;target.DrawingBinding.LastMaterial1=l.MaterialName;}
        if(source is CementedLens d&&target is CementedLens c){c.Diameter=d.Diameter*k;c.MechanicalDiameter=d.MechanicalDiameter*k;c.T1=d.T1*k;c.T2=d.T2*k;c.FrontSurface=Surf(d.FrontSurface);c.ContactSurface=Surf(d.ContactSurface);c.BackSurface=Surf(d.BackSurface);c.Material1=d.Material1;c.Material2=d.Material2;c.Nd1=d.Nd1;c.Nd2=d.Nd2;c.Vd1=d.Vd1;c.Vd2=d.Vd2;target.DrawingBinding.LastMaterial1=d.Material1;target.DrawingBinding.LastMaterial2=d.Material2;}
        target.InvalidateRenderCache();
    }
    public static void SynchronizeGeneratedSheet(IReadOnlyList<Entity> entities)
    {
        var owners=entities.Where(e=>e.DrawingBinding is not null&&(e is OpticalLens||e is CementedLens)).ToList();
        var owner=owners.FirstOrDefault(e=>GeometrySignature(e)!=e.DrawingBinding!.LastGeometrySignature)??owners.FirstOrDefault();
        if(owner is null)return;var info=owner.DrawingBinding!;
        if(owner is OpticalLens l){if(l.MaterialName!=info.LastMaterial1){if(GlassLibrary.Find(l.MaterialName) is not null)l.ApplyMaterial(l.MaterialName);else{l.RefractiveIndex=double.NaN;l.AbbeNumber=double.NaN;}info.LastMaterial1=l.MaterialName;}}
        else if(owner is CementedLens d){if(d.Material1!=info.LastMaterial1){if(GlassLibrary.Find(d.Material1) is not null)d.ApplyMaterial1(d.Material1);else{d.Nd1=double.NaN;d.Vd1=double.NaN;}info.LastMaterial1=d.Material1;}if(d.Material2!=info.LastMaterial2){if(GlassLibrary.Find(d.Material2) is not null)d.ApplyMaterial2(d.Material2);else{d.Nd2=double.NaN;d.Vd2=double.NaN;}info.LastMaterial2=d.Material2;}}
        var source=SourcePartFromView(owner);
        foreach(var other in owners.Where(e=>!ReferenceEquals(e,owner)))CopyScaledGeometry(source,other);
        foreach(var displayed in owners)displayed.DrawingBinding!.LastGeometrySignature=GeometrySignature(displayed);
        foreach(var block in entities.OfType<lcdb.Annotation.AsphericDataBlock>()){
            var single=source as OpticalLens;var glue=source as CementedLens;
            var surf=block.SurfaceLabel=="1"?single?.FrontSurface??glue!.FrontSurface:block.SurfaceLabel=="2"?single?.BackSurface??glue!.ContactSurface:glue?.BackSurface;
            if(surf is not AsphericSurface a)continue;
            double h=a.SemiAperture>0?a.SemiAperture:(single?.Diameter??glue!.Diameter)/2;
            var samples=new[]{0.0,h*.25,h*.5,h*.75,h};
            if(block.BaseRadius==a.Radius&&block.ConicConstant==a.ConicConstant&&block.QuadraticCoefficient==a.QuadraticCoefficient&&block.EvenCoefficients.SequenceEqual(a.EvenCoefficients)&&block.SampleHeights.SequenceEqual(samples))continue;
            block.BaseRadius=a.Radius;block.ConicConstant=a.ConicConstant;block.QuadraticCoefficient=a.QuadraticCoefficient;block.EvenCoefficients=(double[])a.EvenCoefficients.Clone();block.SampleHeights=samples;block.InvalidateRenderCache();
        }
        string date = DrawingDate(entities, info);
        foreach (var displayed in owners) displayed.DrawingBinding!.DrawingDate = date;
        var values=Values(source,info.Name,info.Number,info.Source,SheetScaleLabel(owners.Select(o => o.DrawingBinding!.ViewScale)),date);
        foreach(var entity in entities){
            if(entity.TemplateTextBindings is null||entity.TemplateTextBindings.Count==0)continue;
            bool changed=false;
            foreach(var slot in TextSlots(entity)){
                if(!entity.TemplateTextBindings.TryGetValue(slot.Key,out var binding))continue;
                string current=slot.Get();
                if (binding.OverrideText is { } literal)
                {
                    if (current != literal) { slot.Set(literal); changed = true; }
                    continue;
                }
                binding.Expression=ReconcileExpression(binding,current);
                string rendered=S(binding.Expression,values);binding.LastValues=ExpressionValues(binding.Expression,values);binding.LastRendered=rendered;
                if(current!=rendered){slot.Set(rendered);changed=true;}
            }
            if(changed)entity.InvalidateRenderCache();
        }
    }
    public static List<Entity> RestoreReusableTemplate(IReadOnlyList<Entity> entities)
    {
        // Reconcile the template copy only. The source document may have an independent undo/recovery history.
        var owner=entities.FirstOrDefault(e=>e.DrawingBinding is not null);
        bool oldGenerated=owner is null&&entities.Any(e=>e is OpticalLens||e is CementedLens);
        if(oldGenerated)throw new InvalidOperationException("这张旧版图纸未保存参数绑定。请先重新导入原设计生成加工图，再保存为模板，以免沿用旧半径和材料。");
        var result=new List<Entity>();
        foreach(var e in entities){
            if(e is OpticalLens||e is CementedLens||e.Owner is OpticalLens||e.Owner is CementedLens||(e.IsGenerated&&e.Source==EntitySource.OpticalElement))continue;
            var copy=(Entity)e.Clone();
            if(copy.TemplateTextBindings is not null)foreach(var slot in TextSlots(copy))if(copy.TemplateTextBindings.TryGetValue(slot.Key,out var binding))slot.Set(binding.OverrideText is null ? ReconcileExpression(binding,slot.Get()) : binding.Expression);
            if (copy is DrawingFrame restoredFrame) restoredFrame.SourceFrameLayoutKey = null;
            copy.TemplateTextBindings=null;copy.DrawingBinding=null;copy.InvalidateRenderCache();result.Add(copy);
        }
        if(!result.OfType<TemplateViewRegion>().Any()&&owner?.DrawingBinding is {} info)result.AddRange(info.Regions.Select(r=>(Entity)r.Clone()));
        return result;
    }
}
