using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using LitMath;
using OtoCAD.Avalonia.Commands;
using OtoCAD.Avalonia.Slim;
namespace OtoCAD.Avalonia;

public partial class MainWindow
{
    private CadCanvas _templateCanvas=null!;
    private string? _templatePath;
    private string _designSource="";
    private int _importPartIndex;
    private readonly Dictionary<CadCanvas,string> _sheets=new();
    private static string TemplateDir=>Path.Combine(AppContext.BaseDirectory,"Templates");
    private static string SlimSettingsPath=>Path.Combine(AppContext.BaseDirectory,"Data","template-choice.json");
    private const string FactoryDefaultTemplate = "默认模板.otocad";
    private static readonly (string Name, string Key)[] OriginalTemplates =
    {
        ("ISO三列表", "iso-lens"), ("中文内部图", "gb-lens"), ("GB-my-1", "gb-my-1"),
    };
    private sealed class TemplateChoice
    {
        public string DefaultTemplate { get; set; } = FactoryDefaultTemplate;
    }

    private void InitializeSlimTemplate()
    {
        Directory.CreateDirectory(TemplateDir);
        string defaultPath = Path.Combine(TemplateDir, FactoryDefaultTemplate);
        if (!File.Exists(defaultPath))
        {
            using var seed = typeof(MainWindow).Assembly.GetManifestResourceStream("OtoCAD.DefaultTemplate")!;
            using var destination = File.Create(defaultPath);
            seed.CopyTo(destination);
        }
        foreach (var (name, key) in OriginalTemplates)
        {
            var path = Path.Combine(TemplateDir, name + ".otocad");
            if (!File.Exists(path)) SaveTemplateEntities(TemplatePipeline.OriginalPreset(key), path);
        }

        string selected = FactoryDefaultTemplate;
        try
        {
            if (File.Exists(SlimSettingsPath))
                selected = JsonSerializer.Deserialize<TemplateChoice>(File.ReadAllText(SlimSettingsPath))?.DefaultTemplate
                    ?? selected;
        }
        catch (JsonException) { }
        catch (IOException) { }

        var file = Path.Combine(TemplateDir, Path.GetFileName(selected));
        if (!File.Exists(file)) file = defaultPath;
        _activeCanvas.LoadOtocadFile(file);
        _activeCanvas.FrameCellEditingEnabled = true;
        _templateCanvas = _activeCanvas;
        _templatePath = file;
        UpdateTemplateLabel();
        _status.Text = "在“模板”页选择或编辑模板，再到“出图”页导入和导出。";
    }
    private void RegisterSlimCommands(CommandRegistry reg)
    {
        reg.Register("Slim.TemplateLibrary",()=>_ = OpenTemplateLibraryAsync());
        reg.Register("Slim.NewISO", () => NewSlimTemplate(TemplatePipeline.OriginalPreset("iso-lens")));
        reg.Register("Slim.NewGB", () => NewSlimTemplate(TemplatePipeline.OriginalPreset("gb-lens")));
        reg.Register("Slim.NewGBExample", () => NewSlimTemplate(TemplatePipeline.OriginalPreset("gb-my-1")));
        reg.Register("Slim.NewBlank", () => NewSlimTemplate(TemplatePipeline.Preset("Blank")));
        reg.Register("Slim.SaveTemplate",()=>_ = SaveSlimTemplateAsync());
        reg.Register("Slim.UseCurrent",()=>{_templateCanvas=_activeCanvas;_templatePath=null;UpdateTemplateLabel();_status.Text="当前文档已作为本次导入的模板；保存模板可供以后使用。";});
        reg.Register("Slim.SetDefault",()=>_ = SetDefaultTemplateAsync());
        reg.Register("Slim.Region",()=>{var cmd=new PlaceMarkCmd("零件图区",p=>new TemplateViewRegion{Origin=p,PaperWidth=120,PaperHeight=150});_activeCanvas.StartCommand(cmd);});
        reg.Register("Slim.Table",()=>{var t=DefaultTable();_activeCanvas.StartCommand(new PlaceMarkCmd("自由表格",p=>{var c=(TemplateTable)t.Clone();c.Origin=p;return c;}));});
        reg.Register("Slim.EditTable",()=>_ = EditSlimTableAsync());
        reg.Register("Slim.Layout",()=>{_activeCanvas.FrameCellEditingEnabled=!_activeCanvas.FrameCellEditingEnabled; foreach(var frame in _activeCanvas.GetAllEntities().OfType<DrawingFrame>()){var layer=_activeCanvas.GetEntityLayer(frame);if(layer is not null)layer.IsLocked=_activeCanvas.FrameCellEditingEnabled;}_status.Text=_activeCanvas.FrameCellEditingEnabled?"内容编辑：点击表格单元格或文字，在左侧修改。":"布局编辑：点击整张表格或零件图区后拖动，左侧可改位置和大小。";});
        reg.Register("Slim.Export",()=>_ = ExportSlimAsync());
        foreach(var field in TemplatePipeline.Fields)
        {
            var key=field;reg.Register("Slim.Field."+key,()=>_activeCanvas.StartCommand(new PlaceMarkCmd(key,p=>new Text(TemplatePipeline.Token(key),new Vector3(p.X,p.Y,0),2.5))));
        }
    }
    private static TemplateTable DefaultTable()=>new()
    {
        Origin=new Vector2(20,30),PaperWidth=170,RowHeight=7,TextHeight=2.5,ColumnWidths=new(){1,1,1},
        Columns=new(){new(){Title="参数",Rows=new(){"前表面半径","后表面半径","中心厚度","材料"}},new(){Title="数值",Rows=new(){"【前表面半径】","【后表面半径】","【中心厚度】","【材料】"}},new(){Title="备注",Rows=new(){"","","",""}}}
    };
    private void NewSlimTemplate(IReadOnlyList<Entity> entities)
    {
        var c=CreateDocumentTab("新模板",select:true);c.LoadGeneratedSheet(entities);c.FrameCellEditingEnabled=true;
        _templateCanvas=c;_templatePath=null;UpdateTemplateLabel();_status.Text="新模板已创建；可自由添加文字、线条、表格和零件图区，然后保存模板。";
    }
    private async Task OpenTemplateLibraryAsync()
    {
        var chosen=await SlimDialogs.Library(this,TemplateDir,_templatePath??"");if(chosen is null)return;
        try
        {
            var c=CreateDocumentTab("模板 · "+Path.GetFileNameWithoutExtension(chosen),select:true);c.LoadOtocadFile(chosen);c.FrameCellEditingEnabled=true;
            _templateCanvas=c;_templatePath=chosen;UpdateTemplateLabel();_status.Text="已使用模板："+Path.GetFileNameWithoutExtension(chosen);
        }
        catch(Exception ex){_status.Text="模板读取失败："+ex.Message;}
    }
    private async Task<bool> SaveSlimTemplateAsync()
    {
        var canvas=_activeCanvas;
        string initial=ReferenceEquals(canvas,_templateCanvas)&&_templatePath is not null?Path.GetFileNameWithoutExtension(_templatePath):"我的模板";
        var name=await SlimDialogs.Prompt(this,"保存模板",initial,"输入模板名称；每个名称对应一套可重复使用的图纸模板。");
        if(string.IsNullOrWhiteSpace(name))return false;
        try
        {
            name=SafeName(name.Trim());var path=Path.Combine(TemplateDir,name+".otocad");
            if(File.Exists(path))File.Copy(path,path+".bak",true);
            var entities=TemplatePipeline.RestoreReusableTemplate(canvas.GetAllEntities());
            if(entities.Count==0)throw new InvalidOperationException("模板为空，请先添加图框、文字或表格。");
            SaveTemplateEntities(entities,path);canvas.LoadOtocadFile(path);canvas.AcceptSavedTemplate(path);
            _templateCanvas=canvas;_templatePath=path;UpdateTemplateLabel();_status.Text="模板已保存："+name;return true;
        }
        catch(Exception ex){_status.Text="模板保存失败："+ex.Message;return false;}
    }
    internal static void SaveTemplateEntities(IReadOnlyList<Entity> entities,string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);var db=new Database();
        foreach(var entity in entities)
        {
            var copy=(Entity)entity.Clone();
            if(copy is lcdb.Image img&&!string.IsNullOrEmpty(img.imagePath)&&File.Exists(img.imagePath))
            {
                var assets=Path.Combine(TemplateDir,"Assets");Directory.CreateDirectory(assets);
                string leaf=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(img.imagePath))).Substring(0,20)+Path.GetExtension(img.imagePath);
                var destination=Path.Combine(assets,leaf);if(!File.Exists(destination))File.Copy(img.imagePath,destination);
                img.imagePath=Path.GetRelativePath(AppContext.BaseDirectory,destination);
            }
            db.AddEntity(copy);
        }
        db.SaveAs(path);var check=new Database();check.Open(path);
        if(check.GetEntities().Count()!=entities.Count)throw new IOException("模板保存检查失败：部分实体没有成功保存。");
    }
    private async Task SetDefaultTemplateAsync()
    {
        if(_templatePath is null||!ReferenceEquals(_activeCanvas,_templateCanvas)||_activeCanvas.IsDirty){if(!await SaveSlimTemplateAsync())return;}
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SlimSettingsPath)!);
            File.WriteAllText(SlimSettingsPath,JsonSerializer.Serialize(new TemplateChoice{DefaultTemplate=Path.GetFileName(_templatePath!)}));
            _status.Text="默认模板已设为："+Path.GetFileNameWithoutExtension(_templatePath);
        }
        catch(Exception ex){_status.Text="保存默认模板失败："+ex.Message;}
    }
    private void UpdateTemplateLabel()
    {
        var label=this.FindControl<TextBlock>("CurrentTemplateText");if(label is not null)label.Text="当前模板："+(_templatePath is null?"未保存模板":Path.GetFileNameWithoutExtension(_templatePath));
    }
    private async Task EditSlimTableAsync()
    {
        var table=_activeCanvas.SelectedEntities.OfType<TemplateTable>().FirstOrDefault()??_activeCanvas.GetAllEntities().OfType<TemplateTable>().LastOrDefault();
        if(table is null){_status.Text="请先放置一张自由表格。";return;}
        var dlg=new TableEditorDialog(table);await dlg.ShowDialog(this);
        if(dlg.Result is {} result){table.Columns=result.Columns;table.ColumnWidths=result.ColumnWidths;table.InvalidateRenderCache();_activeCanvas.MarkDirty();_activeCanvas.RequestRedraw();}
    }
    private async Task ExportSlimAsync()
    {
        var open=_docTabs.Items.OfType<TabItem>().Select(t=>t.Content).OfType<CadCanvas>().Where(c=>TemplatePipeline.IsGeneratedDrawing(c.GetAllEntities())).ToList();
        var choice=await SlimDialogs.Export(this,open.Count);if(choice is null)return;
        var folders=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title="选择导出文件夹",AllowMultiple=false});
        if(folders.Count==0)return;
        string? baseFolder=folders[0].Path.LocalPath;if(string.IsNullOrEmpty(baseFolder))return;
        var dir=Path.Combine(baseFolder,"出图_"+DateTime.Now.ToString("yyyyMMdd_HHmmss"));Directory.CreateDirectory(dir);
        var targets=choice.Value.All&&open.Count>0?open:new List<CadCanvas>{_activeCanvas};
        int success=0,failed=0,index=0;var log=new List<string>();
        foreach(var canvas in targets)
        {
            index++;string name=_sheets.TryGetValue(canvas,out var n)?n:canvas.GetAllEntities().FirstOrDefault(e=>e.DrawingBinding is not null)?.DrawingBinding?.Name??(canvas.CurrentFilePath is {} saved?Path.GetFileNameWithoutExtension(saved):"当前图纸");
            var stem=Path.Combine(dir,index.ToString("000")+"_"+SafeName(name));
            foreach(var format in choice.Value.Formats)
            {
                try{TemplatePipeline.Export(canvas,stem,new[]{format});success++;log.Add("成功  "+Path.GetFileName(stem+"."+format));}
                catch(Exception ex){failed++;log.Add("失败  "+format+"："+ex.Message);}
            }
        }
        File.WriteAllLines(Path.Combine(dir,"导出结果.txt"),log);
        _status.Text=$"导出完成：{success} 个文件"+(failed>0?$"，失败 {failed} 个":"")+"；"+dir;
        var message=string.Join("\n",log.Take(8))+"\n\n文件夹："+dir;
        await SlimDialogs.Message(this,$"导出结果 · 成功 {success} / 失败 {failed}",message);
    }
    private static string SafeName(string name)
    {
        foreach(char c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');
        return string.IsNullOrWhiteSpace(name)?"图纸":name;
    }
}
