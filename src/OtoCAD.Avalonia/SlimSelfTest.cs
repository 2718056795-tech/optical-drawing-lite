using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using lcdb.Optic.Import;
using LitMath;
namespace OtoCAD.Avalonia.Slim;

internal static class SlimSelfTest
{
    public static int Run(string fixtureDir,string output,string? codevPath=null)
    {
        Directory.CreateDirectory(output);var log=new List<string>();int checks=0,sheets=0,files=0;
        void Check(bool ok,string message){checks++;if(!ok)throw new InvalidOperationException(message);log.Add("PASS "+message);}
        try
        {
            log.Add("测试条件：官方样例缺少口径时，由测试明确补填轮廓口径10mm；不视为设计实测口径。");
            var inputs=new[]{Path.Combine(fixtureDir,"zmx","cooke_triplet.zmx"),Path.Combine(fixtureDir,"zmx","achromat_doublet.zmx"),Path.Combine(fixtureDir,"optic","cooke_triplet.json")};
            var parts=new List<(string Name,Entity Part)>();
            for(int i=0;i<inputs.Length;i++)
            {
                var p=inputs[i];var data=Path.GetExtension(p)==".zmx"?new ZmxParser().Parse(p):new OpticJsonDeserializer().Parse(File.ReadAllText(p));
                Check(data.IsValid,"设计解析 "+Path.GetFileName(p));
                var b=new PrescriptionToLensBuilder().Build(data,new PrescriptionToLensBuilder.Options{SourceLabel=Path.GetFileName(p),DefaultDiameter=10});
                Check(b.Entities.Count==(i==1?1:3),"导入元件数量 "+b.Entities.Count);
                int j=0;foreach(var e in b.Entities)parts.Add(($"{i+1}-{++j}",e));
            }
            if(codevPath is not null)
            {
                var cp=new CodeVSeqParser();var data=cp.Parse(codevPath);Check(data.IsValid,"用户CODE V读取："+string.Join(";",data.Errors));Check(cp.ConfigurationCount==11,"识别11个CODE V配置");Check(data.Surfaces.Count==24,"识别CODE V物面+22面+像面");
                var build=new PrescriptionToLensBuilder().Build(data,new PrescriptionToLensBuilder.Options{DefaultDiameter=10,SourceLabel=Path.GetFileName(codevPath)});
                Check(build.SingleCount==6&&build.CementedCount==3,"CODE V识别6单片+3双胶合组");var first=(OpticalLens)build.Entities[0];
                Check(Math.Abs(first.R1-21.127)<1e-10&&Math.Abs(first.R2-87.1)<1e-10&&Math.Abs(first.Thickness-1.54)<1e-10,"G1半径与中心厚度对照源文件一致");Check(first.MaterialName=="H-LAK53B"&&first.Extensions.Tags["sourceMaterial1"]=="HLAK53B_CDGM","G1材料匹配H-LAK53B并保存原始名称");
                Check(first.FrontSurface.SemiAperture==0&&first.BackSurface.SemiAperture==0,"文件未提供口径，不伪造表面通光口径");
                Check(Math.Abs(first.RefractiveIndex-1.755)<1e-9&&Math.Abs(first.AbbeNumber-52.322058)<1e-8,"H-LAK53B折射率阿贝数匹配CDGM目录");
                Check(GlassLibrary.Find("HLAK53B_SCHOTT") is null,"材料匹配不跨目录误替代");
                Check(GlassLibrary.Find("HZF52GT_CDGM")?.Name=="H-ZF52GT"&&GlassLibrary.Find("HZF52_CDGM")?.Name=="H-ZF52","GT后缀材料保持独立");
                foreach(var surf in data.Surfaces.Where(x=>x.HasMaterial))Check(GlassLibrary.Find(surf.Glass) is not null,"V49材料目录匹配 "+surf.Glass);
                for(int z=1;z<=11;z++){var conf=cp.Parse(codevPath,z);Check(conf.IsValid&&Math.Abs(conf.Surfaces[1].Radius-21.127)<1e-10&&Math.Abs(conf.Surfaces[1].Thickness-1.54)<1e-10,"G1在Z"+z+"处方保持一致");}
                Check(Math.Abs(cp.Parse(codevPath,11).Surfaces[20].Thickness-3.880627063600999)<1e-10,"CODE V续行与Z11厚度读取正确");
                var mapped=TemplatePipeline.Values(first,"G1","V49-G1",Path.GetFileName(codevPath),"1:1");Check(mapped["前表面半径"]=="R21.127/CX"&&mapped["后表面半径"]=="R87.100/CC","G1凸凹面标识正确");
                Check(mapped["折射率"]=="1.755"&&mapped["阿贝数"]=="52.322","nd/vd显示三位小数，内部目录精度保留");
                File.WriteAllText(Path.Combine(output,"CODEV_G1数据核验.json"),JsonSerializer.Serialize(new{Source=Path.GetFileName(codevPath),SourceSHA256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(codevPath))),ConfigurationCount=11,FirstLens=new{FrontSurface=1,BackSurface=2,R1mm=first.R1,R2mm=first.R2,CenterThicknessmm=first.Thickness,SourceGlass=first.Extensions.Tags["sourceMaterial1"],Glass=first.MaterialName,Nd=first.RefractiveIndex,Vd=first.AbbeNumber,ClearDiameter="文件未提供",MechanicalDiameter="文件未提供"},DrawingTestCondition="按用户要求使用不相交示意口径10mm，不代表文件给定尺寸",Warnings=data.Warnings},new JsonSerializerOptions{WriteIndented=true}));
                Check(TemplatePipeline.TrySetIllustrativeAperture(first)&&first.Diameter==10,"G1示意口径10mm通过不相交检查");
                parts.Add(("CODEV-G1-示意口径10",first));
                int glueIndex=0;foreach(var glue in build.Entities.OfType<CementedLens>()){Check(TemplatePipeline.TrySetIllustrativeAperture(glue),"V49胶合件选择不相交示意口径");parts.Add(("CODEV-胶合"+(++glueIndex),glue));}
                var edge=cp.ParseText("! LEN; S 4 4\nRDM;LEN\nDIM M\nSO 0 1e13\nS 30 2 N-BK7\nCIR 5; CIR EDG 6\nS -30 2\nCIR 5\nSI 0 0\nGO\nIN evil.seq");Check(edge.IsValid&&edge.Surfaces.Count==4&&edge.Surfaces[1].SemiDiameter==5&&edge.Surfaces[1].MechanicalSemiDiameter==6,"CODE V注释/分号/CIR/尾部宏隔离");
                var missing=cp.ParseText("RDM;LEN\nSO 0 1000\nS 30 2 N-BK7\nS -30 2\nSI 0 0\nGO");Check(!missing.IsValid,"CODE V缺少单位时拒绝猜测");
                var macro=cp.ParseText("RDM;LEN\nDIM M\nFOR ^i 1 2\nS 30 2 N-BK7\nSI 0 0\nGO");Check(!macro.IsValid,"CODE V动态宏不能静态执行");
            }
            var asph=new OpticalLens{Diameter=20,Thickness=5,FrontSurface=new AsphericSurface{Radius=38,ConicConstant=-1.1,EvenCoefficients=new[]{1.1e-6,-2e-9},SemiAperture=10},BackSurface=OpticalSurface.Sphere(-55)};
            asph.ApplyMaterial("N-BK7");parts.Add(("非球面",asph));
            var custom=TemplatePipeline.Preset("Blank");
            var region=custom.OfType<TemplateViewRegion>().Single();region.Origin=new Vector2(38,100);region.PaperWidth=134;region.PaperHeight=170;
            var table=new TemplateTable{Origin=new Vector2(18,24),PaperWidth=174,RowHeight=7,TextHeight=2.5,Columns=new(){new(){Title="自定义参数",Rows=new(){"前表面","后表面","厚度","材料"}},new(){Title="自动填入",Rows=new(){"【前表面半径】","【后表面半径】","【中心厚度】","【材料】"}},new(){Title="可编辑备注",Rows=new(){"自定义文字保留","自由模板测试","可任意修改",""}},new(){Title="来源",Rows=new(){"【源文件】","【图号】","【日期】","【比例】"}}},ColumnWidths=new(){1,1.4,1.4,1.3}};
            custom.Add(table);custom.Add(new Text("自由模板 · 【零件名称】",new Vector3(18,282,0),4));custom.Add(new Line(new Vector2(18,278),new Vector2(192,278)));
            var variants=new[]{("ISO",TemplatePipeline.Preset("ISO")),("GB",TemplatePipeline.Preset("GB")),("自由四列",custom),("胶合模板",TemplatePipeline.Preset("Glue"))};
            foreach(var (name,template) in variants)
            {
                string tp=Path.Combine(output,name+"模板.otocad");MainWindow.SaveTemplateEntities(template,tp);var read=new Database();read.Open(tp);var loaded=read.GetEntities().ToList();
                Check(loaded.Count==template.Count,"模板保存再打开 "+name);
                Check(loaded.OfType<TemplateViewRegion>().Count()==1,"零件图区保存 "+name);
                if(name=="自由四列")
                {
                    var t=loaded.OfType<TemplateTable>().Single();Check(t.Columns.Count==4&&t.ColumnWidths.SequenceEqual(table.ColumnWidths),"自由表格四列与列宽保留");
                    t.InvalidateRenderCache();var cell=t.GetCell(0,0);Check(cell.HasValue,"表格单元格可点击命中");
                    t.SetCellText(cell!.Value,"修改后文字");Check(t.GetCellText(cell.Value)=="修改后文字","单元格属性可编辑");
                    t.Columns[0].Rows[0]="前表面";
                }
                string snapshot=JsonSerializer.Serialize(loaded.Select(e=>new{Type=e.GetType().Name,Text=e is Text t?t.Value:e is TemplateTable tt?string.Join("/",tt.Columns.SelectMany(c=>c.Rows)):e is IsoLensDrawingFrame iso?JsonSerializer.Serialize(new{iso.Columns,iso.SpecificationBands,iso.Notes}):e is GbLensDrawingFrame gb?JsonSerializer.Serialize(new{gb.Columns,gb.SpecificationBands,gb.NoteLines}):e is TemplateViewRegion rv?JsonSerializer.Serialize(new{X=rv.Origin.X,Y=rv.Origin.Y,rv.PaperWidth,rv.PaperHeight}):""}));
                foreach(var (partName,part) in parts)
                {
                    var sheet=TemplatePipeline.Build(loaded,part,partName,"TEST-"+partName,"fixture");sheets++;
                    if(partName.StartsWith("CODEV-G1"))
                    {
                        Check(!sheet.OfType<RadialDimension>().Any(),"G1半径在表格中，零件图不重复标注 "+name);
                        var pf=sheet.OfType<DrawingFrame>().First(f=>f is not TemplateTable);var pc=OtoCAD.Avalonia.Templating.Document1Sheet.Columns(pf);
                        if(pc is not null)Check(pc[1].Rows.Any(x=>x.Contains("1.755 ±0.0003"))&&pc[1].Rows.Any(x=>x.Contains("52.322 ±0.5%")),"参考图材料公差默认文字 "+name);
                    }
                    Check(!sheet.OfType<TemplateViewRegion>().Any(),"图区已替换 "+name+partName);
                    Check(sheet.Count(e=>e is OpticalLens||e is CementedLens)==1,"每张图一个光学件 "+name+partName);
                    var sf=sheet.OfType<DrawingFrame>().First(f=>f is not TemplateTable);var sc=OtoCAD.Avalonia.Templating.Document1Sheet.Columns(sf);
                    if(sc is not null)
                    {
                        var zone=(IPropertyZoneFrame)sf;int bands=sf is IsoLensDrawingFrame ii?ii.SpecificationBands:((GbLensDrawingFrame)sf).SpecificationBands;
                        if(part is CementedLens cl)
                        {
                            Check(bands==2&&sc.Count==6,"胶合自动扩展上下两组三列表 "+name+partName);
                            var values=TemplatePipeline.Values(cl,"","","","");Check(sc[2].Rows.Any(x=>x.Contains(values["胶合面片1半径"]))&&sc[3].Rows.Any(x=>x.Contains(values["胶合面片2半径"])),"胶合面两侧半径及凹凸标识 "+name+partName);
                            Check(sc[1].Rows.Any(x=>x.Contains(cl.Material1))&&sc[4].Rows.Any(x=>x.Contains(cl.Material2)),"两片材料分别填表 "+name+partName);
                            Check(sc[1].Rows.Any(x=>x.Contains(values["折射率1"]+" ±0.0003"))&&sc[4].Rows.Any(x=>x.Contains(values["阿贝数2"]+" ±0.5%")),"两片材料精度与公差 "+name+partName);
                            var cell=zone.GetCell(4,0);Check(cell.HasValue,"第二组材料单元格可命中 "+name+partName);string before=zone.GetCellText(cell!.Value),firstValue=sc[1].Rows[0];zone.SetCellText(cell.Value,"第二片编辑测试");Check(sc[4].Rows[0]=="第二片编辑测试"&&sc[1].Rows[0]==firstValue,"第二组编辑不改第一组 "+name+partName);zone.SetCellText(cell.Value,before);
                            Check(sheet.OfType<LinearDimension>().Any(x=>x.userText==$"T1 {cl.T1:F2}")&&sheet.OfType<LinearDimension>().Any(x=>x.userText==$"T2 {cl.T2:F2}"),"双胶合分片厚度标注 "+name+partName);
                        }
                        else Check(bands==1&&sc.Count==3,"单片保持三列表 "+name+partName);
                    }
                    if(name=="自由四列")
                    {
                        var t=sheet.OfType<TemplateTable>().Single();Check(t.Columns.Count==4&&t.Columns[2].Rows[0]=="自定义文字保留","自定义格式和固定文字继承 "+partName);
                        Check(!t.Columns.SelectMany(c=>c.Rows).Any(x=>x.Contains('【')),"设计参数全部填入 "+partName);
                        var source=loaded.OfType<TemplateViewRegion>().Single();var view=sheet.First(e=>e is OpticalLens||e is CementedLens);var pos=view is OpticalLens vl?vl.Position:((CementedLens)view).Position;
                        Check(view.bounding.left>=source.Origin.X-1e-8&&view.bounding.right<=source.Origin.X+source.PaperWidth+1e-8,"自定义图区边界继承 "+partName);
                    }
                    if(part is OpticalLens src&&src.FrontSurface is AsphericSurface)
                    {
                        var display=sheet.OfType<OpticalLens>().Single();double k=display.Diameter/src.Diameter;
                        Check(display.FrontSurface is AsphericSurface,"非球面保留面型 "+name);
                        Check(Math.Abs(display.FrontSurface.Sag(6*k)-src.FrontSurface.Sag(6)*k)<1e-9,"非球面缩放矢高正确 "+name);
                        Check(sheet.OfType<lcdb.Annotation.AsphericDataBlock>().Any(),"非球面参数块存在 "+name);
                    }
                    bool hasFront=TemplatePipeline.TableIncludesParameter(TemplatePipeline.AdaptReferenceTemplate(loaded,part is CementedLens),"前表面半径"),hasBack=TemplatePipeline.TableIncludesParameter(TemplatePipeline.AdaptReferenceTemplate(loaded,part is CementedLens),"后表面半径");
                    int expectedRadials=part is CementedLens&&name=="自由四列"?1:0;
                    Check(sheet.OfType<RadialDimension>().Count()==expectedRadials,"仅保留表格未包含的半径标注 "+name+partName);
                    Check(sheet.OfType<LinearDimension>().Count()>=3,"去除重复R标注后保留厚度口径尺寸 "+name+partName);
                    var shape=sheet.First(e=>e is OpticalLens||e is CementedLens);var bodyBox=shape.bounding;
                    foreach(var dim in sheet.OfType<LinearDimension>())
                    {
                        if(Math.Abs(Math.Cos(dim.rotation))<1e-3)Check(dim.dimLinePosition.X>bodyBox.right,"口径标注位于轮廓右侧 "+name+partName);
                        else {Check(dim.textReferencePoint.Y<bodyBox.bottom||dim.textReferencePoint.Y>bodyBox.top,"厚度文字放到轮廓外 "+name+partName);Check(Math.Abs(dim.style.ExtensionLineOffset-0.6)<1e-8,"尺寸延伸线使用0.6mm小间隙 "+name+partName);}
                    }
                    if(name=="ISO"&&partName=="CODEV-G1-示意口径10")Check(((OpticalLens)shape).Diameter/((OpticalLens)part).Diameter>13,"单片比旧版10:1显著放大");
                    if(name=="ISO"&&partName=="CODEV-胶合1")Check(((CementedLens)shape).Diameter/((CementedLens)part).Diameter>8,"胶合比旧版5:1显著放大");
                    var canvas=new CadCanvas();canvas.LoadGeneratedSheet(sheet);var stem=Path.Combine(output,name+"_"+partName);
                    TemplatePipeline.Export(canvas,stem,new[]{"dxf","pdf","png","otocad","json"});files+=5;
                    foreach(string ext in new[]{"dxf","pdf","png","otocad","json"})Check(new FileInfo(stem+"."+ext).Length>100,"输出非空 "+name+partName+"."+ext);
                    foreach(string ext in new[]{"otocad","json"}){var db=new Database();db.Open(stem+"."+ext);Check(db.GetEntities().Count()==sheet.Count,"原生格式读回无丢失 "+name+partName+"."+ext);if(part is CementedLens&&sc is not null){var ff=db.GetEntities().OfType<DrawingFrame>().First(f=>f is not TemplateTable);Check((ff is IsoLensDrawingFrame si?si.SpecificationBands:((GbLensDrawingFrame)ff).SpecificationBands)==2&&OtoCAD.Avalonia.Templating.Document1Sheet.Columns(ff)!.Count==6,"胶合表格保存重开保留双层 "+ext);}if(part is OpticalLens al&&al.FrontSurface is AsphericSurface)Check(db.GetEntities().OfType<OpticalLens>().Single().FrontSurface is AsphericSurface,"非球面保存重开保留面型 "+ext);}
                    int n=netDxf.DxfDocument.Load(stem+".dxf").Entities.All.Count();Check(n>40,"DXF实体读回 "+n);
                    TemplatePipeline.Export(canvas,stem+"_repeat",new[]{"otocad","dxf"});files+=2;
                    var repeat=new Database();repeat.Open(stem+"_repeat.otocad");Check(repeat.GetEntities().Count()==sheet.Count,"连续保存不丢实体 "+name+partName);Check(netDxf.DxfDocument.Load(stem+"_repeat.dxf").Entities.All.Count()==n,"连续DXF导出计数一致 "+name+partName);
                }
                string after=JsonSerializer.Serialize(loaded.Select(e=>new{Type=e.GetType().Name,Text=e is Text t?t.Value:e is TemplateTable tt?string.Join("/",tt.Columns.SelectMany(c=>c.Rows)):e is IsoLensDrawingFrame iso?JsonSerializer.Serialize(new{iso.Columns,iso.SpecificationBands,iso.Notes}):e is GbLensDrawingFrame gb?JsonSerializer.Serialize(new{gb.Columns,gb.SpecificationBands,gb.NoteLines}):e is TemplateViewRegion rv?JsonSerializer.Serialize(new{X=rv.Origin.X,Y=rv.Origin.Y,rv.PaperWidth,rv.PaperHeight}):""}));Check(snapshot==after,"重复导入不修改源模板 "+name);
            }
            var noRadiusTemplate=TemplatePipeline.Preset("Blank");var simplePart=new OpticalLens{Diameter=10,Thickness=5,R1=30,R2=-40};
            var noRadiusSheet=TemplatePipeline.Build(noRadiusTemplate,simplePart,"no-radius-table","","test");Check(noRadiusSheet.OfType<RadialDimension>().Count()==2,"模板未放半径参数时保留图中R标注");
            var w=new MainWindow();w.Show();Dispatcher.UIThread.RunJobs();
            w.ValidateSlimUI(log,output);
            foreach(var size in new[]{new Size(1280,820),new Size(980,680)})
            {
                w.Width=size.Width;w.Height=size.Height;w.Measure(size);w.Arrange(new Rect(size));Dispatcher.UIThread.RunJobs();w.ActiveCanvas.ResetView();Dispatcher.UIThread.RunJobs();
                using var bitmap=new RenderTargetBitmap(new PixelSize((int)size.Width,(int)size.Height));bitmap.Render(w);bitmap.Save(Path.Combine(output,$"界面_{size.Width}.png"));
            }
            w.Close();log.Add($"SUCCESS checks={checks}; sheets={sheets}; files={files}; templates={variants.Length}");
            File.WriteAllLines(Path.Combine(output,"验证报告.txt"),log);return 0;
        }
        catch(Exception ex){log.Add("FAIL "+ex);File.WriteAllLines(Path.Combine(output,"验证报告.txt"),log);return 1;}
    }
}

