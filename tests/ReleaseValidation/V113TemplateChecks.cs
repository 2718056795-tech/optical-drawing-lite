using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using OtoCAD.Avalonia;
using OtoCAD.Avalonia.Commands;
using OtoCAD.Avalonia.Services;
using OtoCAD.Avalonia.Slim;

internal static class V113TemplateChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static IReadOnlyList<string> Run(string output)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool ok,string name)
        {
            if (!ok) throw new InvalidOperationException("模板回归："+name);
            checks.Add("PASS "+name);
        }
        VerifyDrawing(output,Check);
        VerifyEditedPlacement(output,Check);
        VerifyWorkspace(output,Check);
        VerifyTemplateEditors(Check);
        VerifyStartup(Check);
        return checks;
    }

    private static OpticalLens Lens(double radius,string material)
    {
        var lens=new OpticalLens{R1=radius,R2=-45,Diameter=10,Thickness=4};
        lens.FrontSurface.SemiAperture=lens.BackSurface.SemiAperture=5;
        lens.ApplyMaterial(material);
        return lens;
    }

    private static void VerifyDrawing(string output,Action<bool,string> Check)
    {
        var a=Lens(30,"N-BK7");
        var b=Lens(95,"H-LAK53B");
        var template=TemplatePipeline.Preset("ISO");
        var sheet=TemplatePipeline.Build(template,a,"A","A-1","A.zmx");
        var resheet=TemplatePipeline.Build(sheet,b,"B","B-1","B.zmx");
        var frame=resheet.OfType<IsoLensDrawingFrame>().Single();
        Check(frame.Columns[0].Rows[0].Contains("95.000")&&frame.Columns[1].Rows[0].Contains("H-LAK53B"),
            "直接将 A 成品传入出图引擎，B 的表格写入 R95.000/H-LAK53B");

        var dateBinding=frame.TemplateTextBindings["p:DrawDate"];
        frame.DrawDate=dateBinding.LastRendered=dateBinding.LastValues["日期"]="2004-03-02";
        resheet.OfType<OpticalLens>().Single().DrawingBinding.DrawingDate="";
        var c=new CadCanvas();
        c.LoadGeneratedSheet(resheet);
        string dated=Path.Combine(output,"dated.otocad");
        c.SaveCurrentScene(dated);
        var reopened=new CadCanvas();
        reopened.LoadOtocadFile(dated);
        TemplatePipeline.Export(reopened,Path.Combine(output,"dated-reexport"),new[]{"json","pdf"});
        Check(reopened.GetAllEntities().OfType<IsoLensDrawingFrame>().Single().DrawDate=="2004-03-02",
            "旧版日期绑定经重开和再次导出仍为 2004-03-02");

        var multi=TemplatePipeline.Preset("ISO");
        var region=multi.OfType<TemplateViewRegion>().Single();
        region.UseFullPaperArea=false;region.Origin=new LitMath.Vector2(20,120);region.PaperWidth=125;region.PaperHeight=74;region.ViewScale=1.25;
        var second=(TemplateViewRegion)region.Clone();
        second.Origin=new LitMath.Vector2(20,202);second.ViewScale=2.5;multi.Add(second);
        var views=TemplatePipeline.Build(multi,a,"视图","V","A.zmx");
        var lenses=views.OfType<OpticalLens>().ToList();
        lenses[1].Thickness+=.25;
        TemplatePipeline.SynchronizeGeneratedSheet(views);
        Check(views.OfType<IsoLensDrawingFrame>().Single().DrawScale=="见各视图"
            &&views.OfType<Text>().Any(t=>t.Value.EndsWith("1.25:1"))&&views.OfType<Text>().Any(t=>t.Value.EndsWith("2.5:1")),
            "各视图的 1.25:1、2.5:1 和标题栏在编辑第二个视图后对应一致");
        second.ViewScale=40;
        bool oversized=false;
        try { TemplatePipeline.Build(multi,a,"视图","V","A.zmx"); }
        catch(InvalidOperationException ex) { oversized=ex.Message.Contains("固定比例"); }
        Check(oversized,"40:1 镜片及尺寸超出指定图区，生成前返回明确提示");

        string longNote=string.Concat(Enumerable.Repeat("左右表面镀增透膜 RAVG 0.5% 420–680 nm。",16));
        frame.Notes=longNote+"\n第二条";frame.InvalidateRenderCache();
        double width=frame.PaperWidth-2*frame.BorderMargin-56;
        var lines=PaperTextLayout.Wrap(frame.Notes,width,frame.CellTextHeight,frame.UseEngineeringSerif);
        Check(lines.Count>4 && lines.All(line=>PaperTextLayout.Width(line.Text,frame.CellTextHeight,frame.UseEngineeringSerif)<=width+.0001)
            &&string.Concat(lines.Where(line=>line.Paragraph==0).Select(line=>line.Text))==longNote,"图纸毫米宽度折行，混合中英文条款完整无截断");
        Check(PaperTextLayout.Width("WWWWWW",2.5)>2*PaperTextLayout.Width("iiiiii",2.5),"字体实测区分宽窄拉丁字形");
        double expanded=frame.MeasuredNotesHeight();
        var notesCanvas=new CadCanvas();notesCanvas.LoadGeneratedSheet(resheet);
        TemplatePipeline.Export(notesCanvas,Path.Combine(output,"wrapped-notes"),new[]{"pdf","dxf","png","json","otocad"});
        frame.Notes="短条款";frame.InvalidateRenderCache();
        Check(frame.MeasuredNotesHeight()<expanded&&frame.MeasuredNotesHeight()==frame.NotesRowHeight,"缩短条款后回到模板设置的最低行高");

    }

    private static void VerifyEditedPlacement(string output,Action<bool,string> check)
    {
        var template=TemplatePipeline.Preset("ISO");
        var region=template.OfType<TemplateViewRegion>().Single();
        region.UseFullPaperArea=false;region.Origin=new LitMath.Vector2(32,116);
        region.PaperWidth=128;region.PaperHeight=74;region.ViewScale=1.25;
        var lens=Lens(1000,"H-LAK53B");lens.R2=-1000;
        var sheet=TemplatePipeline.Build(template,lens,"可移动固定视图","F","frame.zmx");
        var canvas=new CadCanvas();canvas.LoadGeneratedSheet(sheet);
        var view=sheet.OfType<OpticalLens>().Single();
        view.Translate(new LitMath.Vector2(25,50));
        string moved=Path.Combine(output,"manually-moved");
        TemplatePipeline.Export(canvas,moved,new[]{"json"});
        check(File.Exists(moved+".json"),"固定视图在纸内手动平移后可导出");
        var target=Path.Combine(output,"too-large-after-edit");
        double old=view.Diameter;
        view.Diameter=45*view.DrawingBinding.ViewScale;view.MechanicalDiameter=view.Diameter;view.InvalidateRenderCache();
        bool oversized=false;
        try {TemplatePipeline.Export(canvas,target,new[]{"pdf","json"});}
        catch(InvalidOperationException ex){oversized=ex.Message.Contains("固定比例");}
        check(oversized && !File.Exists(target+".pdf") && !File.Exists(target+".json"),
            "编辑后通光直径从 10 增至 45 mm，越出固定图区时在写出前拦截");
        view.Diameter=old;view.MechanicalDiameter=old;view.InvalidateRenderCache();
        view.DrawingBinding.FixedBodyHeight=null;view.DrawingBinding.FixedBodyWidth=null;view.DrawingBinding.Regions.Clear();
        TemplatePipeline.Export(canvas,Path.Combine(output,"legacy-region"),new[]{"json"});
        view.Translate(new LitMath.Vector2(500,0));bool outside=false;
        try { TemplatePipeline.Export(canvas,target,new[]{"json"}); }
        catch(InvalidOperationException ex){ outside=ex.Message.Contains("超出纸张"); }
        check(outside && !File.Exists(target+".json"),"缺少固定图区数据的旧图允许纸内摆放，超纸镜片仍会被识别");
        var spareLayer=canvas.GetOrCreateLayer("备用镜片");spareLayer.IsFrozen=true;
        canvas.AssignEntityToLayer(view,spareLayer);
        var main=Lens(1000,"N-BK7");
        main.Position=new LitMath.Vector2(92,170);
        canvas.AddEntitiesBatch(new[]{main});
        string hidden=Path.Combine(output,"hidden-spare");
        TemplatePipeline.Export(canvas,hidden,new[]{"pdf","otocad"});
        var archived=new CadCanvas();archived.LoadOtocadFile(hidden+".otocad");
        var allLenses=archived.GetAllEntities().OfType<OpticalLens>().ToList();
        var spare=allLenses.Single(l=>!archived.IsEntityVisible(l));
        check(File.Exists(hidden+".pdf") && allLenses.Count(archived.IsEntityVisible)==1 && spare.bounding.left>210,
            "冻结图层中移出纸面的备用镜片不阻止出图，原生文件仍保留镜片");

    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Wait(Func<bool> ready)
    {
        for(int n=0;n<300&&!ready();n++){Pump();Thread.Sleep(5);}
        if(!ready()) throw new TimeoutException("等待模板窗口响应超时。");
    }
    private static void Click(Window dialog,string label)
    {
        dialog.GetVisualDescendants().OfType<Button>().Single(btn=>Equals(btn.Content,label))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
    }
    private static MainWindow OpenWindow()
    {
        var window=new MainWindow{ShowInTaskbar=false,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual,Position=new PixelPoint(-20000,-20000)};
        window.Show();Pump();return window;
    }
    private static void CloseWindow(MainWindow window)
    {
        typeof(MainWindow).GetField("UnsavedPromptOverride",Hidden)!.SetValue(window, (Func<string,Task<string?>>)(_=>Task.FromResult<string?>("不保存")));
        window.Close();Pump();
    }
    private static void VerifyWorkspace(string output,Action<bool,string> Check)
    {
        var a=Lens(30,"N-BK7");
        var b=Lens(95,"H-LAK53B");
        var template=TemplatePipeline.Preset("ISO");
        var win=OpenWindow();
        string testName="template-regression-"+Guid.NewGuid().ToString("N");
        string saved=Path.Combine(AppDataPaths.TemplatesDirectory,testName+".otocad");
        try
        {
            void Command(string key) => ((CommandRegistry)typeof(MainWindow).GetField("_currentRegistry",Hidden)!.GetValue(win)!).Resolve(key)!();
            void Add(string name,OpticalLens part) =>
                typeof(MainWindow).GetMethods(Hidden).Single(m=>m.Name.EndsWith("IAppHost.AddElementSheetTab")).Invoke(win,new object[]{name,part});
            Window Dialog(){Wait(()=>win.OwnedWindows.Count==1);return win.OwnedWindows.Single();}
            void NameTemplate()
            {
                Command("Slim.SaveTemplate");var dialog=Dialog();
                dialog.GetVisualDescendants().OfType<TextBox>().Single().Text=testName;Click(dialog,"确定");
            }
            win.ActiveCanvas.LoadGeneratedSheet(template);
            Add("A",a);Command("Slim.UseCurrent");Add("B",b);
            var current=win.ActiveCanvas;
            var activeFrame=current.GetAllEntities().OfType<IsoLensDrawingFrame>().Single();
            Check(activeFrame.Columns[0].Rows[0].Contains("95.000")&&activeFrame.Columns[1].Rows[0].Contains("H-LAK53B"),
                "实际“当前作为模板”命令保留 B 的半径与玻璃");
            current.SaveCurrentScene(Path.Combine(output,"original.otocad"));
            string? priorPath=current.CurrentFilePath;
            current.ApplyEdit(()=>activeFrame.Notes="本次编辑，可撤销");
            NameTemplate();Wait(()=>File.Exists(saved));Pump();
            Check(ReferenceEquals(current,win.ActiveCanvas)&&current.IsDirty&&current.CurrentFilePath==priorPath
                &&current.GetAllEntities().OfType<OpticalLens>().Count()==1&&current.Undo(),
                "成品另存模板保留原图标签、镜片、路径、未存状态及撤销");
            byte[] original=File.ReadAllBytes(saved);
            NameTemplate();Click(Dialog(),"取消");Pump();
            Check(File.ReadAllBytes(saved).SequenceEqual(original),"跨文档同名保存必须确认，取消覆盖保留原文件");
            var chosen=win.SidebarDocumentTabs.Single(t=>t.Content is CadCanvas sheetCanvas && sheetCanvas.CurrentFilePath==saved);
            Command("Slim.NewBlank");Pump();
            var draft=win.ActiveCanvas;
            draft.LoadGeneratedSheet(TemplatePipeline.Preset("ISO"));
            var draftFrame=draft.GetAllEntities().OfType<IsoLensDrawingFrame>().Single();
            draft.ApplyEdit(()=>draftFrame.Notes="新模板覆盖后可见的条款");
            NameTemplate();Click(Dialog(),"覆盖");
            Wait(()=>ReferenceEquals(win.ActiveCanvas,chosen.Content));
            Check(win.SidebarDocumentTabs.Count(t=>t.Content is CadCanvas doc && doc.CurrentFilePath==saved)==1
                && draft.CurrentFilePath is null && draft.IsDirty
                && ((CadCanvas)chosen.Content!).GetAllEntities().OfType<IsoLensDrawingFrame>().Single().Notes=="新模板覆盖后可见的条款",
                "未命名草稿覆盖已打开的模板时复用原标签并刷新，草稿不重复绑定磁盘路径");
            win.SelectSidebarDocument(chosen);Pump();
            var edit=(CadCanvas)chosen.Content!;
            var editedFrame=edit.GetAllEntities().OfType<IsoLensDrawingFrame>().Single();
            edit.ApplyEdit(()=>editedFrame.Notes="放弃的临时镀膜要求");
            var close=win.CloseSidebarDocumentAsync(chosen);Click(Dialog(),"不保存");Wait(()=>close.IsCompleted);
            Check(!win.SidebarDocumentTabs.Contains(chosen),"实际“不保存”按钮关闭模板");
            Add("C",a);
            Check(win.ActiveCanvas.GetAllEntities().OfType<IsoLensDrawingFrame>().Single().Notes!="放弃的临时镀膜要求",
                "已关闭模板放弃的镀膜要求不会进入下一枚图纸");
        }
        finally {CloseWindow(win); File.Delete(saved);File.Delete(saved+".bak");}
    }

    private static void VerifyTemplateEditors(Action<bool,string> check)
    {
        var win=OpenWindow();
        try
        {
            void Command(string key) =>
                ((CommandRegistry)typeof(MainWindow).GetField("_currentRegistry",Hidden)!.GetValue(win)!).Resolve(key)!();
            Command("Slim.TemplateLibrary");
            Wait(()=>win.OwnedWindows.Count==1);
            var library=win.OwnedWindows.Single();
            var list=library.GetVisualDescendants().OfType<ListBox>().Single();
            check(list.Items.Cast<string>().Any(name=>name.Contains("本次使用")&&name.Contains("启动默认")),
                "模板库分别标记本次模板与启动默认模板");
            Click(library,"取消");
            var first=new TemplateTable {Columns=new(){new IsoSpecColumn{Title="甲表",Rows=new(){"材料旧值后缀"}}},ColumnWidths=new(){1}};
            var second=new TemplateTable {Columns=new(){new IsoSpecColumn{Title="乙表",Rows=new(){"不变"}}},ColumnWidths=new(){1}};
            win.ActiveCanvas.LoadGeneratedSheet(new Entity[]{first,second});
            Command("Slim.EditTable");Pump();
            check(win.OwnedWindows.Count==0,"有多张表格且未选中时不会编辑最后一张");
            win.ActiveCanvas.Select(first);
            Command("Slim.EditTable");
            Wait(()=>win.OwnedWindows.Count==1);
            var editor=win.OwnedWindows.Single();
            var box=editor.GetVisualDescendants().OfType<TextBox>().Single(t=>Equals(t.Watermark,"第1行"));
            box.Focus();box.CaretIndex=2;box.SelectionStart=2;box.SelectionEnd=4;
            editor.GetVisualDescendants().OfType<ComboBox>().Single().SelectedItem="材料";
            Click(editor,"插入设计参数");
            Click(editor,"应用表格");Pump();
            check(first.Columns[0].Rows[0]=="材料【材料】后缀" && second.Columns[0].Rows[0]=="不变",
                "在选定表格以参数替换光标选区，另一张表和后缀保留");
        }
        finally {CloseWindow(win);}
    }

    private static void VerifyStartup(Action<bool,string> Check)
    {
        string settings=Path.Combine(AppDataPaths.DataDirectory,"template-choice.json");
        byte[]? savedSettings=File.Exists(settings)?File.ReadAllBytes(settings):null;
        string badName="broken-template-"+Guid.NewGuid().ToString("N")+".otocad";
        string badPath=Path.Combine(AppDataPaths.TemplatesDirectory,badName);
        try
        {
            File.WriteAllText(badPath,"damaged fixture");
            File.WriteAllText(settings,JsonSerializer.Serialize(new{DefaultTemplate=badName}));
            var recovery=OpenWindow();
            try{Check(recovery.ActiveCanvas.GetAllEntities().Count>0 && File.ReadAllText(badPath)=="damaged fixture",
                "损坏的启动模板保留原文件并回退至可用模板");}
            finally{CloseWindow(recovery);}
        }
        finally
        {
            if(savedSettings is null)File.Delete(settings);else File.WriteAllBytes(settings,savedSettings);
            File.Delete(badPath);
        }
    }
}
