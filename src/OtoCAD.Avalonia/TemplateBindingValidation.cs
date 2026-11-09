using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using LitMath;
namespace OtoCAD.Avalonia.Slim;
public static class TemplateBindingValidation
{
    public static IReadOnlyList<string> Run(string output)
    {
        Directory.CreateDirectory(output);var log=new List<string>();
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("Binding test: "+message);log.Add("PASS "+message);}
        var source=new OpticalLens{R1=30,R2=-40,Thickness=4,Diameter=10};source.ApplyMaterial("N-BK7");
        var template=TemplatePipeline.Preset("ISO");
        template.Add(new Text("中心厚度【中心厚度】 mm",new Vector3(20,90,0),2.5));
        var sheet=TemplatePipeline.Build(template,source,"第一枚","A1","sample.zmx");
        var view=sheet.OfType<OpticalLens>().Single();double scale=view.DrawingBinding!.ViewScale;
        Check(scale>1&&Math.Abs(TemplatePipeline.SourcePartFromView(view).AsLens().Thickness-4)<1e-9,"显示比例可还原真实中心厚度");
        view.Thickness=5*scale;view.MaterialName="H-LAK53B";view.InvalidateRenderCache();
        var frame=sheet.OfType<IsoLensDrawingFrame>().Single();frame.Notes="用户修改的三条技术要求\n第二条新要求\n第三条新要求";
        frame.Columns[1].Rows[1]=frame.Columns[1].Rows[1].Replace("±0.0003","±0.0004");
        TemplatePipeline.SynchronizeGeneratedSheet(sheet);
        Check(sheet.OfType<Text>().Any(x=>x.Value=="中心厚度5 mm"),"改中心厚度后文本按真实尺寸更新");
        Check(frame.Columns[1].Rows[0].Contains("H-LAK53B")&&frame.Columns[1].Rows[1].Contains("1.755")&&frame.Columns[1].Rows[1].Contains("±0.0004"),"改材料同步目录参数并保留自定义公差文字");
        frame.Columns[0].Rows[0]="曲率半径 R999.000/CX";TemplatePipeline.SynchronizeGeneratedSheet(sheet);
        Check(frame.Columns[0].Rows[0].Contains("30.000")&&!frame.Columns[0].Rows[0].Contains("999.000"),"直接改设计数字文字不会冻结参数绑定");
        frame.InsertRow(0,0,"新增加工说明");TemplatePipeline.SynchronizeGeneratedSheet(sheet);
        Check(frame.Columns[0].Rows[0]=="新增加工说明"&&frame.Columns[0].Rows[1].Contains("30.000"),"参数表插入技术行后绑定跟随原参数行");
        frame.RemoveRow(0,0);TemplatePipeline.SynchronizeGeneratedSheet(sheet);
        Check(frame.Columns[0].Rows[0].Contains("30.000"),"删除技术行后参数绑定保持一致");
        var restored=TemplatePipeline.RestoreReusableTemplate(sheet);
        var rf=restored.OfType<IsoLensDrawingFrame>().Single();
        Check(restored.OfType<TemplateViewRegion>().Count()==1&&!restored.Any(x=>x is OpticalLens||x is LinearDimension),"成品恢复模板含图区且剔除零件及标注");
        Check(rf.Columns[1].Rows[1].Contains("【折射率】")&&rf.Columns[1].Rows[1].Contains("±0.0004")&&rf.Notes==frame.Notes,"恢复占位符且保留用户技术文字");
        var second=new OpticalLens{R1=100,R2=-60,Thickness=6,Diameter=10};second.ApplyMaterial("H-K9L");
        var sheet2=TemplatePipeline.Build(restored,second,"第二枚","B2","second.zmx");
        var f2=sheet2.OfType<IsoLensDrawingFrame>().Single();
        Check(f2.Columns[0].Rows[0].Contains("100.000")&&f2.Columns[1].Rows[0].Contains("H-K9L")&&sheet2.OfType<Text>().Any(x=>x.Value=="中心厚度6 mm"),"复用模板自动替换另一枚半径材料厚度");
        foreach(string ext in new[]{"otocad","json"}){
            var path=Path.Combine(output,"绑定重开."+ext);MainWindow.SaveTemplateEntities(sheet,path);var db=new Database();db.Open(path);var reopened=db.GetEntities().ToList();
            var rv=reopened.OfType<OpticalLens>().Single();Check(rv.DrawingBinding is not null,"保存重开保留真实比例与参数来源 "+ext);
            rv.R1=45*rv.DrawingBinding!.ViewScale;rv.InvalidateRenderCache();TemplatePipeline.SynchronizeGeneratedSheet(reopened);
            Check(reopened.OfType<IsoLensDrawingFrame>().Single().Columns[0].Rows[0].Contains("45.000"),"重开后修改半径仍同步表格 "+ext);
            var rt=TemplatePipeline.RestoreReusableTemplate(reopened);
            Check(!rt.Any(x=>x is LinearDimension||x is lcdb.Annotation.CoatingMark)&&rt.OfType<TemplateViewRegion>().Count()==1,"重开后另存模板无残留旧图标注 "+ext);
            Check(TemplatePipeline.IsGeneratedDrawing(reopened),"重开加工图可纳入全部导出 "+ext);
        }
        var custom=TemplatePipeline.Preset("ISO");var cf=custom.OfType<IsoLensDrawingFrame>().Single();cf.Columns[0].Title="入射面自定义";cf.Columns[1].Title="玻璃来料要求";cf.Columns[0].Rows[2]="倒角： 0.20";cf.Columns[2].Rows[4]="4/ 35″";
        var dc=TemplatePipeline.AdaptReferenceTemplate(custom,true).OfType<IsoLensDrawingFrame>().Single();
        Check(dc.Columns[0].Title=="入射面自定义"&&dc.Columns[1].Title=="玻璃来料要求"&&dc.Columns[3].Rows.Contains("倒角： 0.20")&&dc.Columns[2].Rows.Contains("4/ 35″"),"双胶合转换保留自定义列名倒角技术参数");
        var mult=TemplatePipeline.Preset("Blank");var region=mult.OfType<TemplateViewRegion>().Single();region.ViewScale=2;var region2=(TemplateViewRegion)region.Clone();region2.ViewScale=3;region2.Origin=new Vector2(90,100);mult.Add(region2);mult.Add(new Text("【中心厚度】",new Vector3(20,20,0),2));
        var multi=TemplatePipeline.Build(mult,source,"multi","M1","sample.zmx");var vs=multi.OfType<OpticalLens>().ToArray();vs[1].Thickness=7*3;TemplatePipeline.SynchronizeGeneratedSheet(multi);
        Check(Math.Abs(vs[0].Thickness-14)<1e-9&&Math.Abs(vs[1].Thickness-21)<1e-9&&multi.OfType<Text>().Any(x=>x.Value=="7"),"多视图不同图面比例下设计修改同步");
        var invalid=TemplatePipeline.Build(template,source,"invalid","I1","sample.zmx");var invalidView=invalid.OfType<OpticalLens>().Single();invalidView.Thickness=-invalidView.DrawingBinding!.ViewScale;
        var canvas=new CadCanvas();canvas.LoadGeneratedSheet(invalid);bool rejected=false;var badStem=Path.Combine(output,"invalid-should-not-write");
        try{TemplatePipeline.Export(canvas,badStem,new[]{"json"});}catch(InvalidOperationException){rejected=true;}
        Check(rejected&&!File.Exists(badStem+".json"),"编辑为无效厚度时导出被阻止且不写错误图纸");
        var one=new OpticalLens{R1=50,R2=-60,Thickness=1,Diameter=4};one.ApplyMaterial("N-BK7");
        var proseTemplate=TemplatePipeline.Preset("ISO");proseTemplate.Add(new Text("厚度【中心厚度】 ±0.1",new Vector3(20,90,0),2.5));
        var proseSheet=TemplatePipeline.Build(proseTemplate,one,"prose","P1","test.zmx");var proseText=proseSheet.OfType<Text>().Single(x=>x.Value.StartsWith("厚度"));proseText.Value+="（检验）";
        TemplatePipeline.SynchronizeGeneratedSheet(proseSheet);Check(proseText.Value=="厚度1 ±0.1（检验）","重复数字与公差中的数字不导致用户后缀丢失");
        var reusableProse=TemplatePipeline.RestoreReusableTemplate(proseSheet);one.Thickness=2;
        var proseSecond=TemplatePipeline.Build(reusableProse,one,"prose2","P2","test.zmx");Check(proseSecond.OfType<Text>().Any(x=>x.Value=="厚度2 ±0.1（检验）"),"重复数字场景另存模板再导入只替换设计值");
        var mismatch=new OpticalLens{R1=30,R2=-40,Thickness=5,Diameter=10};mismatch.FrontSurface.SemiAperture=6;bool badAperture=false;try{TemplatePipeline.ValidatePart(mismatch);}catch(InvalidOperationException){badAperture=true;}
        Check(badAperture,"逐面通光口径大于整体口径时拒绝静默不一致");
        return log;
    }
    private static OpticalLens AsLens(this Entity e)=>(OpticalLens)e;
}
