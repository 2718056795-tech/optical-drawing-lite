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
using OtoCAD.Avalonia.Services;
using Avalonia.Threading;
namespace OtoCAD.Avalonia;

public partial class MainWindow
{
    private CadCanvas _templateCanvas=null!;
    private string? _templatePath;
    private string _designSource="";
    private int _importPartIndex;
    private readonly Dictionary<CadCanvas,string> _sheets=new();
    private static string TemplateDir => AppDataPaths.TemplatesDirectory;
    private static string SlimSettingsPath => Path.Combine(AppDataPaths.DataDirectory, "template-choice.json");
    private bool _exportInProgress;
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
        AppDataPaths.CreateFileIfMissing(defaultPath, temporary =>
        {
            using var seed = typeof(MainWindow).Assembly.GetManifestResourceStream("OtoCAD.DefaultTemplate")!;
            using var destination = File.Create(temporary);
            seed.CopyTo(destination);
            destination.Flush(flushToDisk: true);
        });
        foreach (var (name, key) in OriginalTemplates)
        {
            var path = Path.Combine(TemplateDir, name + ".otocad");
            AppDataPaths.CreateFileIfMissing(path, temporary => SaveTemplateEntities(TemplatePipeline.OriginalPreset(key), temporary));
        }

        _templateCanvas = _activeCanvas;
        var warning = LoadAvailableTemplate(_activeCanvas, StartupTemplatePath());
        UpdateTemplateLabel();
        _status.Text = string.Join("\n", AppDataPaths.MigrationWarnings.Append(warning ??
            "在“模板”页选择或编辑模板，再到“出图”页导入和导出。"));
    }

    private static string StartupTemplatePath()
    {
        string? selected = null;
        try
        {
            if (File.Exists(SlimSettingsPath))
                selected = JsonSerializer.Deserialize<TemplateChoice>(File.ReadAllText(SlimSettingsPath))?.DefaultTemplate;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        if (string.IsNullOrWhiteSpace(selected) || Path.GetFileName(selected) != selected) selected = FactoryDefaultTemplate;
        return Path.Combine(TemplateDir, selected);
    }

    private static bool SameFile(string? first, string? second) =>
        !string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(second) &&
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

    private static void LoadTemplate(CadCanvas canvas, string path)
    {
        if (canvas.LoadOtocadFile(path) == 0) throw new InvalidDataException("模板中没有图框、零件图区或文字。");
        canvas.FrameCellEditingEnabled = true;
    }

    private string? LoadAvailableTemplate(CadCanvas canvas, string requested)
    {
        string factory = Path.Combine(TemplateDir, FactoryDefaultTemplate);
        foreach (var path in new[]{requested, factory}.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                LoadTemplate(canvas, path);
                _templatePath = path;
                return SameFile(path, requested) ? null : $"无法读取“{Path.GetFileName(requested)}”，已使用“{FactoryDefaultTemplate}”。";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
        }
        // The user's library and its backups remain intact even if both templates are damaged.
        string recovery = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".otocad");
        try
        {
            using (var seed = typeof(MainWindow).Assembly.GetManifestResourceStream("OtoCAD.DefaultTemplate")!)
            using (var file = File.Create(recovery)) seed.CopyTo(file);
            canvas.LoadOtocadFile(recovery, asRecovery:true);
            canvas.FrameCellEditingEnabled = true;
            _templatePath = null;
        }
        finally { File.Delete(recovery); }
        return $"无法读取“{Path.GetFileName(requested)}”，已打开随程序提供的模板副本，可另存为新模板。";
    }

    private void RegisterSlimCommands(CommandRegistry reg)
    {
        reg.Register("Slim.TemplateLibrary",()=>_ = OpenTemplateLibraryAsync());
        reg.Register("Slim.NewISO", () => NewSlimTemplate(TemplatePipeline.OriginalPreset("iso-lens")));
        reg.Register("Slim.NewGB", () => NewSlimTemplate(TemplatePipeline.OriginalPreset("gb-lens")));
        reg.Register("Slim.NewGBExample", () => NewSlimTemplate(TemplatePipeline.OriginalPreset("gb-my-1")));
        reg.Register("Slim.NewBlank", () => NewSlimTemplate(TemplatePipeline.Preset("Blank")));
        reg.Register("Slim.SaveTemplate",()=>_ = SaveSlimTemplateAsync());
        reg.Register("Slim.UseCurrent",UseCurrentAsTemplate);
        reg.Register("Slim.SetDefault",()=>_ = SetDefaultTemplateAsync());
        reg.Register("Slim.Region",()=>{var cmd=new PlaceMarkCmd("零件图区",p=>new TemplateViewRegion{Origin=p,PaperWidth=120,PaperHeight=150});_activeCanvas.StartCommand(cmd);});
        reg.Register("Slim.Table",()=>{var t=DefaultTable();_activeCanvas.StartCommand(new PlaceMarkCmd("自由表格",p=>{var c=(TemplateTable)t.Clone();c.Origin=p;return c;}));});
        reg.Register("Slim.EditTable",()=>_ = EditSlimTableAsync());
        reg.Register("Slim.Layout",()=>{_activeCanvas.FrameCellEditingEnabled=!_activeCanvas.FrameCellEditingEnabled; foreach(var frame in _activeCanvas.GetAllEntities().OfType<DrawingFrame>()){var layer=_activeCanvas.GetEntityLayer(frame);if(layer is not null)layer.IsLocked=_activeCanvas.FrameCellEditingEnabled;}RefreshSidebarMode();_status.Text=_activeCanvas.FrameCellEditingEnabled?"内容编辑：点击表格单元格或文字，在左侧修改。":"布局编辑：点击整张表格或零件图区后拖动，左侧可改位置和大小。";});
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
    private void UseCurrentAsTemplate()
    {
        try
        {
            if (!ReferenceEquals(_activeCanvas,_templateCanvas))
            {
                var entities = TemplatePipeline.RestoreReusableTemplate(_activeCanvas.GetAllEntities());
                if (entities.Count == 0) throw new InvalidOperationException("当前文档没有可复用的模板内容。");
                var copy = CreateDocumentTab("本次模板",select:false);
                copy.LoadGeneratedSheet(entities);
                copy.FrameCellEditingEnabled = true;
                _templateCanvas = copy;
                _templatePath = null;
            }
            UpdateTemplateLabel();
            _status.Text = "后续导入将使用本次模板。";
        }
        catch(Exception ex) { _status.Text = "使用模板失败："+ex.Message; }
    }

    private async Task OpenTemplateLibraryAsync()
    {
        string? chosen = await SlimDialogs.Library(this,TemplateDir,_templatePath??"",StartupTemplatePath());
        if (chosen is null) return;
        try
        {
            var open = _docTabs.Items.OfType<TabItem>().FirstOrDefault(t=>t.Content is CadCanvas c && SameFile(c.CurrentFilePath,chosen));
            if (open?.Content is CadCanvas canvas)
            {
                _docTabs.SelectedItem = open;
                _templateCanvas = canvas;
            }
            else
            {
                var db = new Database();
                db.Open(chosen);
                if (!db.GetEntities().Any()) throw new InvalidDataException("模板为空。");
                var c = CreateDocumentTab("模板 · "+Path.GetFileNameWithoutExtension(chosen),select:true);
                LoadTemplate(c,chosen);
                _templateCanvas=c;
            }
            _templatePath=chosen;
            UpdateTemplateLabel();
            _status.Text="本次使用模板："+Path.GetFileNameWithoutExtension(chosen);
        }
        catch(Exception ex){_status.Text="模板读取失败："+ex.Message;}
    }

    private Task<bool> SaveSlimTemplateAsync() => SaveTemplateFromCanvasAsync(_activeCanvas);

    private async Task<bool> SaveTemplateFromCanvasAsync(CadCanvas canvas)
    {
        bool editing = ReferenceEquals(canvas,_templateCanvas) && !TemplatePipeline.IsGeneratedDrawing(canvas.GetAllEntities());
        string initial = editing && _templatePath is not null ? Path.GetFileNameWithoutExtension(_templatePath) : "我的模板";
        var name = await SlimDialogs.Prompt(this,"保存模板",initial,"输入名称后即可在模板库重复使用。");
        if (string.IsNullOrWhiteSpace(name)) return false;
        try
        {
            name=SafeName(name.Trim());
            var path=Path.Combine(TemplateDir,name+".otocad");
            bool ownFile = editing && SameFile(path,_templatePath);
            var entities=TemplatePipeline.RestoreReusableTemplate(canvas.GetAllEntities());
            if(entities.Count==0)throw new InvalidOperationException("模板为空，请先添加图框、文字或表格。");
            var targetTab = _docTabs.Items.OfType<TabItem>().FirstOrDefault(t=>t.Content is CadCanvas c
                && !ReferenceEquals(c,canvas) && SameFile(c.CurrentFilePath,path));
            var target = targetTab?.Content as CadCanvas;
            if (target?.IsDirty == true)
                throw new InvalidOperationException($"“{name}”已打开并有未保存的修改。请先保存该模板，或选择新名称。");
            if (File.Exists(path))
            {
                if (!ownFile && await Dialogs.ConfirmDialog.ShowAsync(this,"覆盖已有模板",
                    $"模板库中已有“{name}”。要用当前内容覆盖吗？","覆盖","取消") != "覆盖") return false;
            }
            (target ?? canvas).WriteDocumentFile(path, () => SaveTemplateEntities(entities, path));
            if (target is not null)
            {
                LoadTemplate(target,path);
                _templateCanvas=target;
                if (editing && ReferenceEquals(canvas,_activeCanvas)) SelectSidebarDocument(targetTab!);
            }
            else if (editing && (ownFile || _templatePath is null))
            {
                canvas.AcceptSavedTemplate(path);
                _templateCanvas=canvas;
            }
            else
            {
                var copy=CreateDocumentTab("模板 · "+name,select:false);
                LoadTemplate(copy,path);
                _templateCanvas=copy;
            }
            _templatePath=path;
            UpdateTemplateLabel();
            _status.Text="模板已保存："+name;
            return true;
        }
        catch(Exception ex){_status.Text="模板保存失败："+ex.Message;return false;}
    }

    internal static void SaveTemplateEntities(IReadOnlyList<Entity> entities,string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);var db=new Database();
        foreach(var entity in entities)
        {
            var copy=(Entity)entity.Clone();
            if (copy is lcdb.Image image) image.StorePortableImage();
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
            lcdb.IO.AtomicFile.Write(SlimSettingsPath, stream =>
                JsonSerializer.Serialize(stream, new TemplateChoice { DefaultTemplate = Path.GetFileName(_templatePath!) }));
            UpdateTemplateLabel();
            _status.Text="默认模板已设为："+Path.GetFileNameWithoutExtension(_templatePath);
        }
        catch(Exception ex){_status.Text="保存默认模板失败："+ex.Message;}
    }
    private void UpdateTemplateLabel()
    {
        var label=this.FindControl<TextBlock>("CurrentTemplateText");if(label is not null)label.Text="当前模板："+(_templatePath is null?"未保存模板":Path.GetFileNameWithoutExtension(_templatePath));
        RefreshSidebarMode();
    }
    private async Task EditSlimTableAsync()
    {
        var selected=_activeCanvas.SelectedEntities.OfType<TemplateTable>().ToList();
        var tables=_activeCanvas.GetAllEntities().OfType<TemplateTable>().ToList();
        if (selected.Count>1 || (selected.Count==0 && tables.Count>1))
        {
            _status.Text="请先选择要编辑的那张自由表格，再点击“编辑表格”。";
            return;
        }
        var table=selected.FirstOrDefault()??tables.FirstOrDefault();
        if(table is null){_status.Text="请先放置一张自由表格。";return;}
        var dlg=new TableEditorDialog(table);await dlg.ShowDialog(this);
        if (dlg.Result is { } result)
            _activeCanvas.ApplyEdit(() =>
            {
                table.Columns = result.Columns;
                table.ColumnWidths = result.ColumnWidths;
                table.InvalidateRenderCache();
            });
    }
    private sealed record ExportFile(CadCanvas Canvas, string Stem, string Format)
    {
        public string Name => Path.GetFileName(Stem) + "." + Format;
        public bool Done { get; set; }
        public string? Error { get; set; }
    }

    private async Task ExportSlimAsync()
    {
        if (_exportInProgress) return;
        CommitPropertyInput();
        var options = ScopeOptions();
        if (options.All(option => option.Count == 0)) { _status.Text = "请先导入设计或打开加工图。"; return; }
        _exportInProgress = true;
        UpdateBatchActions();
        try
        {
            var settings = AppSettings.Load();
            var choice = await SlimDialogs.Export(this, options, _scope, settings.ExportFormats ?? []);
            if (choice is null) return;
            var targets = GeneratedDrawingTabs(choice.Value.Scope);
            if (targets.Count == 0) return;
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择导出文件夹", AllowMultiple = false });
            if (folders.Count == 0) return;
            string? baseFolder = folders[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(baseFolder)) throw new IOException("请选择本机可写的导出文件夹。");
            string dir = Path.Combine(baseFolder, "出图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(dir);
            settings.ExportFormats = choice.Value.Formats;
            settings.Save();
            await ExportFilesToDirectoryAsync(targets, choice.Value.Formats, dir);
        }
        catch (Exception ex)
        {
            _status.Text = "导出未完成：" + ex.Message;
            await SlimDialogs.Message(this, "导出未完成", ex.Message);
        }
        finally { _exportInProgress = false; UpdateBatchActions(); }
    }

    internal async Task ExportFilesToDirectoryAsync(IReadOnlyList<TabItem> targets, IReadOnlyList<string> formats, string dir)
    {
        var jobs = new List<ExportFile>();
        for (int i = 0; i < targets.Count; i++)
        {
            var canvas = (CadCanvas)targets[i].Content!;
            string name = _sheets.TryGetValue(canvas, out var sheetName) ? sheetName
                : canvas.GetAllEntities().FirstOrDefault(entity => entity.DrawingBinding is not null)?.DrawingBinding?.Name
                ?? (canvas.CurrentFilePath is { } saved ? Path.GetFileNameWithoutExtension(saved) : "当前图纸");
            string stem = Path.Combine(dir, (i + 1).ToString("000") + "_" + SafeName(name));
            jobs.AddRange(formats.Select(format => new ExportFile(canvas, stem, format)));
        }

        var pending = jobs.ToList();
        while (pending.Count > 0)
        {
            await RunExportFilesAsync(pending);
            var failed = jobs.Where(job => job.Error is not null).ToList();
            var lines = failed.Select(job => $"失败  {job.Name}：{job.Error}")
                .Concat(jobs.Where(job => !job.Done && job.Error is null).Select(job => "未处理  " + job.Name))
                .Concat(jobs.Where(job => job.Done).Select(job => "成功  " + job.Name)).ToList();
            string summary = $"成功 {jobs.Count(job => job.Done)}，失败 {failed.Count}，未处理 {jobs.Count(job => !job.Done && job.Error is null)}";
            try { File.WriteAllLines(Path.Combine(dir, "导出结果.txt"), lines); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { lines.Insert(0, "记录写入失败：" + ex.Message); }
            _status.Text = summary + " · " + dir;
            if (!await SlimDialogs.ExportResult(this, dir, lines, failed.Count, summary)) break;
            pending = failed;
        }
    }

    private async Task RunExportFilesAsync(IReadOnlyList<ExportFile> jobs)
    {
        bool canceled = false, finished = false;
        var label = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        var progress = new ProgressBar { Minimum = 0, Maximum = jobs.Count, Height = 8 };
        var cancel = new Button { Content = "停止导出", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right };
        void Stop() { canceled = true; cancel.IsEnabled = false; label.Text = "正在完成当前文件…"; }
        cancel.Click += (_, _) => Stop();
        var window = new Window
        {
            Title = "导出图纸", Width = 430, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new global::Avalonia.Thickness(20), Spacing = 14, Children = { label, progress, cancel } }
        };
        window.Closing += (_, e) => { if (!finished) { e.Cancel = true; Stop(); } };
        var modal = window.ShowDialog(this);
        try
        {
            for (int i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                label.Text = $"已处理 {i} / {jobs.Count} 个文件\\n正在导出 {job.Name}";
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                if (canceled) break;
                string target = job.Stem + "." + job.Format;
                bool alreadyExists = File.Exists(target);
                try
                {
                    TemplatePipeline.Export(job.Canvas, job.Stem, [job.Format]);
                    job.Done = true;
                    job.Error = null;
                }
                catch (Exception ex)
                {
                    job.Error = ex.Message;
                    if (!alreadyExists && File.Exists(target))
                    {
                        try { File.Delete(target); }
                        catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                        { job.Error += "；未能移除不完整文件：" + cleanup.Message; }
                    }
                }
                progress.Value = i + 1;
            }
        }
        finally { finished = true; window.Close(); await modal; }
    }

    private static string SafeName(string name)
    {
        foreach(char c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');
        return string.IsNullOrWhiteSpace(name)?"图纸":name;
    }
}
