using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Automation;
using OtoCAD.Avalonia.Ribbon;
using Avalonia.Layout;
using Avalonia.Media;

namespace OtoCAD.Avalonia;

internal enum DrawingScope { LatestBatch, Current, Selected, All }
internal sealed record DrawingScopeOption(DrawingScope Scope, string Label, int Count)
{
    public override string ToString() => $"{Label}（{Count}）";
}


public partial class MainWindow
{
    private ListBox? _sidebarDocuments;
    private Control? _sidebarDocumentPanel;
    private Control? _sidebarProperties;
    private bool _syncingSidebarDocuments;
    private bool _clearingDrawings;
    private bool _updatingScope;
    private DrawingScope _scope = DrawingScope.All;
    private int _lastBatchId;
    private string _batchName = "";
    private readonly Dictionary<CadCanvas, (int Batch, string Source, bool Overview)> _documentBatches = new();
    private readonly HashSet<CadCanvas> _checkedSheets = new();
    private static readonly (DrawingScope Scope, string Label)[] ScopeNames =
    [
        (DrawingScope.LatestBatch, "本次导入"), (DrawingScope.Current, "当前图纸"),
        (DrawingScope.Selected, "已勾选"), (DrawingScope.All, "全部加工图")
    ];

    internal void BeginDrawingBatch(string sourceName)
    {
        _lastBatchId++;
        _batchName = System.IO.Path.GetFileName(sourceName);
        _scope = DrawingScope.LatestBatch;
    }

    internal void RegisterBatchDocument(CadCanvas canvas, bool overview)
    {
        _documentBatches[canvas] = (_lastBatchId, _batchName, overview);
        RefreshSidebarDocuments();
    }

    private void ForgetBatchDocument(CadCanvas canvas)
    {
        _documentBatches.Remove(canvas);
        _checkedSheets.Remove(canvas);
        _sheets.Remove(canvas);
    }

    private void CommitPropertyInput()
    {
        if (FocusManager?.GetFocusedElement() is TextBox { Tag: Action commit })
            commit();
    }

    internal void RefreshSidebarMode()
    {
        if (_frameModeBanner is null || _activeCanvas is null) return;
        bool frame = _activeCanvas.CurrentEditMode == lcdb.EntityEditMode.Frame;
        _frameModeBanner.IsVisible = ActiveSidebarPage == "模板" || frame;
        var label = this.FindControl<TextBlock>("EditingModeText");
        if (label is null) return;
        string title = _activeCanvas.CurrentFilePath is { } path ? System.IO.Path.GetFileNameWithoutExtension(path)
            : _docTabs.SelectedItem is TabItem tab && _sidebarDocumentNames.TryGetValue(tab, out var name) ? name : "图纸";
        string mode = frame ? "图框模式 · Esc 返回绘图" : _activeCanvas.FrameCellEditingEnabled ? "内容编辑" : "布局编辑";
        label.Text = $"{mode} · {title}";
    }

    private readonly Dictionary<TabItem, string> _sidebarDocumentNames = new();
    private readonly Dictionary<string, Button> _sidebarPageButtons = new();
    public string ActiveSidebarPage { get; private set; } = "出图";
    public IReadOnlyList<TabItem> SidebarDocumentTabs => _docTabs.Items.OfType<TabItem>().ToArray();

    private void InitializeSidebarWorkspace()
    {
        var workspace = this.FindControl<Grid>("WorkspaceGrid")!;
        var side = this.FindControl<Border>("LeftSidebar")!;
        var splitter = this.FindControl<GridSplitter>("SidebarDivider")!;
        var restore = this.FindControl<Button>("RestoreSidebar")!;
        var column = workspace.ColumnDefinitions[0];
        double rememberedWidth = column.Width.Value;
        void SetSidebarVisible(bool visible)
        {
            if (!visible) rememberedWidth = Math.Max(250, column.ActualWidth);
            side.IsVisible = splitter.IsVisible = visible;
            restore.IsVisible = !visible;
            column.MinWidth = visible ? 250 : 0;
            column.Width = new GridLength(visible ? Math.Min(rememberedWidth, column.MaxWidth) : 0);
            workspace.ColumnDefinitions[1].Width = new GridLength(visible ? 5 : 0);
        }
        column.MinWidth = 250;
        workspace.SizeChanged += (_, e) => column.MaxWidth = Math.Max(250, e.NewSize.Width - 235);
        this.FindControl<Button>("CollapseSidebar")!.Click += (_, _) => SetSidebarVisible(false);
        restore.Click += (_, _) => SetSidebarVisible(true);
        this.FindControl<Button>("CurrentTemplateLink")!.Click += (_, _) =>
        {
            SelectSidebarPage("模板");
            _currentRegistry.Resolve("Slim.TemplateLibrary")?.Invoke();
        };
        var scope = this.FindControl<ComboBox>("SidebarScope")!;
        scope.SelectionChanged += (_, _) =>
        {
            if (_updatingScope || scope.SelectedItem is not DrawingScopeOption selected) return;
            _scope = selected.Scope;
            UpdateBatchActions();
        };
        _sidebarDocuments = this.FindControl<ListBox>("SidebarDocuments")!;
        _sidebarDocumentPanel = this.FindControl<Grid>("SidebarDocumentPanel")!;
        _sidebarProperties = this.FindControl<Grid>("SidebarProperties")!;
        _propertyPanel.Width = double.NaN;
        foreach (var entry in new[]
        {
            ("出图", "SidebarDrawingButton"), ("图纸", "SidebarDocumentsButton"),
            ("模板", "SidebarTemplateButton"), ("属性", "SidebarPropertiesButton")
        })
        {
            var button = this.FindControl<Button>(entry.Item2)!;
            string page = entry.Item1;
            button.Tag = page;
            button.Click += (_, _) => SelectSidebarPage(page);
            _sidebarPageButtons[page] = button;
        }
        _sidebarDocuments.SelectionChanged += (_, _) =>
        {
            if (_syncingSidebarDocuments) return;
            if (_sidebarDocuments.SelectedItem is ListBoxItem { Tag: TabItem tab })
                SelectSidebarDocument(tab);
        };
        void WireFileAction(string name, string command, string glyph)
        {
            var button = this.FindControl<Button>(name)!;
            button.Tag = command;
            button.Click += (_, _) => _currentRegistry.Resolve(command)?.Invoke();
            SetLabeledIcon(button, glyph, 15);
        }
        this.FindControl<Button>("SidebarAdoptSerif")!.Click += (_, _) =>
        {
            if (_activeCanvas.AdoptEngineeringSerif())
                _status.Text = "当前图已套用新版字体，可撤销。保存后生效。";
            RefreshSerifAction();
        };
        WireFileAction("SidebarOpenDocument", "File.Open", "open");
        WireFileAction("SidebarSaveDocument", "File.Save", "save");
        WireFileAction("SidebarSaveAsDocument", "File.SaveAs", "save-as");
        var clear = this.FindControl<Button>("SidebarClearDrawings")!;
        clear.Click += async (_, _) => await ClearGeneratedDrawingsAsync(scope: _scope);
        this.FindControl<Button>("SidebarExportScope")!.Click += async (_, _) => await ExportSlimAsync();
        SetLabeledIcon(clear, "clear-sheets");
        SetLabeledIcon(this.FindControl<Button>("FvbZoomExtents")!, "fit", 15);
        SetLabeledIcon(this.FindControl<Button>("FvbZoomSelected")!, "select-fit", 15);
        SetLabeledIcon(this.FindControl<ToggleButton>("SbGridToggle")!, "grid", 14);
        SetLabeledIcon(this.FindControl<ToggleButton>("SbSnapToggle")!, "snap", 14);
        SetIconButton(this.FindControl<Button>("FvbZoomIn")!, "zoom-in", "放大");
        SetIconButton(this.FindControl<Button>("FvbZoomOut")!, "zoom-out", "缩小");
        RefreshSidebarDocuments();
        SelectSidebarPage("出图");
    }

    private static void SetLabeledIcon(ContentControl control, string glyph, double size = 17)
    {
        control.ContentTemplate = new FuncDataTemplate<string>((text, _) => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Children =
            {
                new ToolIcon(glyph, size),
                new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
            },
        });
    }

    private static void SetIconButton(Button button, string glyph, string name)
    {
        button.Content = new ToolIcon(glyph, 15);
        AutomationProperties.SetName(button, name);
    }

    public void SelectSidebarPage(string page)
    {
        if (_sidebarPageButtons.Count == 0) return;
        if (!_sidebarPageButtons.ContainsKey(page)) throw new ArgumentException("未知侧栏页面", nameof(page));
        if (ActiveSidebarPage == "模板" && page != "模板")
            _activeCanvas.CancelPendingPlacement();
        ActiveSidebarPage = page;
        foreach (var entry in _sidebarPageButtons)
        {
            if (entry.Key == page) entry.Value.Classes.Add("active");
            else entry.Value.Classes.Remove("active");
        }
        bool commands = page is "出图" or "模板";
        if (_ribbon is not null)
        {
            _ribbon.IsVisible = commands;
            if (commands) _ribbon.ActivateTabByTitle(page == "出图" ? "出图流程" : "模板编辑");
        }
        _sidebarDocumentPanel!.IsVisible = page == "图纸";
        _sidebarProperties!.IsVisible = page == "属性";
        RefreshSidebarMode();
        if (page == "图纸") RefreshSidebarDocuments();
        else UpdateBatchActions();
    }

    private void ShowImportedDrawings()
    {
        SelectSidebarPage("图纸");
        var list = _sidebarDocuments!;
        list.UpdateLayout();
        if (list.SelectedItem is ListBoxItem selected) list.ScrollIntoView(selected);
        list.Focus();
    }

    public void SelectSidebarDocument(TabItem tab)
    {
        if (!_docTabs.Items.Contains(tab)) return;
        _docTabs.SelectedItem = tab;
        SyncSidebarDocumentSelection();
    }

    public Task CloseSidebarDocumentAsync(TabItem tab) => CloseTabAsync(tab);

    private List<TabItem> GeneratedDrawingTabs(DrawingScope scope = DrawingScope.All) => _docTabs.Items.OfType<TabItem>()
        .Where(tab => tab.Content is CadCanvas canvas && !ReferenceEquals(canvas, _templateCanvas)
            && (!_documentBatches.TryGetValue(canvas, out var batch) || !batch.Overview)
            && Slim.TemplatePipeline.IsGeneratedDrawing(canvas.GetAllEntities()) && (scope switch
            {
                DrawingScope.Current => ReferenceEquals(canvas, _activeCanvas),
                DrawingScope.LatestBatch => _lastBatchId > 0 && batch.Batch == _lastBatchId,
                DrawingScope.Selected => _checkedSheets.Contains(canvas),
                _ => true
            })).ToList();

    private DrawingScopeOption[] ScopeOptions() => ScopeNames
        .Select(item => new DrawingScopeOption(item.Scope, item.Label, GeneratedDrawingTabs(item.Scope).Count)).ToArray();

    private void UpdateBatchActions()
    {
        if (_sidebarDocuments is null || _updatingScope) return;
        var options = ScopeOptions();
        var selected = options.Single(item => item.Scope == _scope);
        _updatingScope = true;
        try
        {
            var scope = this.FindControl<ComboBox>("SidebarScope")!;
            scope.ItemsSource = options;
            scope.SelectedItem = selected;
            var clear = this.FindControl<Button>("SidebarClearDrawings")!;
            clear.Content = $"清空（{selected.Count}）";
            clear.IsEnabled = selected.Count > 0 && !_clearingDrawings;
            this.FindControl<Button>("SidebarExportScope")!.IsEnabled = selected.Count > 0 && !_exportInProgress;
            _ribbon?.SetExportEnabled(options.Single(item => item.Scope == DrawingScope.All).Count > 0);
        }
        finally { _updatingScope = false; }
    }

    public Task<int> ClearGeneratedDrawingsAsync(Func<int,int,Task<bool>>? confirmDiscard = null) =>
        ClearGeneratedDrawingsAsync(DrawingScope.All, confirmDiscard);

    /// <summary>Close generated sheets as one batch; never remove templates or saved files.</summary>
    private async Task<int> ClearGeneratedDrawingsAsync(DrawingScope scope, Func<int,int,Task<bool>>? confirmDiscard = null)
    {
        if (_clearingDrawings) return 0;
        CommitPropertyInput();
        var targets = GeneratedDrawingTabs(scope);
        if (targets.Count == 0) { _status.Text = "没有需要清空的加工图。"; return 0; }
        _clearingDrawings = true;
        RefreshSidebarDocuments();
        try
        {
            int dirty = targets.Count(tab => ((CadCanvas)tab.Content!).IsDirty);
            if (dirty > 0)
            {
                bool accepted = confirmDiscard is not null ? await confirmDiscard(targets.Count,dirty)
                    : await Dialogs.ConfirmDialog.ShowAsync(this,"清空加工图",
                        $"将关闭 {targets.Count} 张加工图，其中 {dirty} 张有未保存内容。\n\n清空后未保存的修改将丢弃。当前模板和磁盘上已保存的文件会保留。",
                        "清空加工图","取消") == "清空加工图";
                if (!accepted) return 0;
            }
            var survivor = _docTabs.SelectedItem is TabItem active && !targets.Contains(active) ? active : _docTabs.Items.OfType<TabItem>().FirstOrDefault(tab => ReferenceEquals(tab.Content,_templateCanvas))
                ?? _docTabs.Items.OfType<TabItem>().FirstOrDefault(tab => !targets.Contains(tab));
            if (survivor is null)
            {
                var template = _templateCanvas.GetAllEntities().Select(e => (lcdb.Entity)e.Clone()).ToList();
                var canvas = CreateDocumentTab("模板",true);
                canvas.LoadGeneratedSheet(template); canvas.FrameCellEditingEnabled = true;
                _templateCanvas = canvas; UpdateTemplateLabel();
                survivor = _docTabs.Items.OfType<TabItem>().Single(tab => ReferenceEquals(tab.Content,canvas));
            }
            _docTabs.SelectedItem = survivor;
            foreach (var tab in targets)
            {
                if (tab.Content is CadCanvas canvas)
                {
                    ReleaseDocumentRecovery(canvas, discardBackup: true);
                    ForgetBatchDocument(canvas);
                }
                _docTabs.Items.Remove(tab); _sidebarDocumentNames.Remove(tab);
                (tab.Content as IDisposable)?.Dispose();
            }
            RefreshSidebarDocuments(); SelectSidebarPage("图纸");
            _status.Text = $"已关闭 {targets.Count} 张加工图。";
            return targets.Count;
        }
        finally { _clearingDrawings = false; RefreshSidebarDocuments(); }
    }

    private void SyncSidebarDocumentSelection()
    {
        if (_sidebarDocuments is null) return;
        _syncingSidebarDocuments = true;
        try
        {
            _sidebarDocuments.SelectedItem = _sidebarDocuments.Items.OfType<ListBoxItem>()
                .FirstOrDefault(row => ReferenceEquals(row.Tag, _docTabs.SelectedItem));
        }
        finally { _syncingSidebarDocuments = false; }
    }

    private void RefreshSerifAction()
    {
        if (this.FindControl<Button>("SidebarAdoptSerif") is { } action)
            action.IsVisible = _activeCanvas?.CanAdoptEngineeringSerif() == true;
    }

    private void RefreshSidebarDocuments()
    {
        if (_sidebarDocuments is null) return;
        RefreshSerifAction();
        UpdateBatchActions();
        _syncingSidebarDocuments = true;
        try
        {
            _sidebarDocuments.Items.Clear();
            var generated = GeneratedDrawingTabs().ToHashSet();
            string Group(TabItem tab)
            {
                var canvas = (CadCanvas)tab.Content!;
                if (_documentBatches.TryGetValue(canvas, out var batch)) return $"{batch.Source} · 第 {batch.Batch} 批";
                if (generated.Contains(tab))
                {
                    string? source = canvas.GetAllEntities().FirstOrDefault(e => e.DrawingBinding is not null)?.DrawingBinding?.Source;
                    return string.IsNullOrWhiteSpace(source) ? "已打开的加工图" : "已打开 · " + System.IO.Path.GetFileName(source);
                }
                return "模板与其他文档";
            }
            foreach (var group in _docTabs.Items.OfType<TabItem>().GroupBy(Group))
            {
                _sidebarDocuments.Items.Add(new ListBoxItem
                {
                    IsEnabled = false, Focusable = false, MinHeight = 24, Padding = new Thickness(4, 3),
                    Content = new TextBlock { Text = group.Key, FontSize = 11, FontWeight = FontWeight.SemiBold }
                });
                foreach (var tab in group)
                {
                    var canvas = (CadCanvas)tab.Content!;
                    string title = canvas?.CurrentFilePath is string path
                        ? System.IO.Path.GetFileNameWithoutExtension(path)
                        : _sidebarDocumentNames.TryGetValue(tab, out string? stored) ? stored : "图纸";
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), MinHeight = 32 };
                    var label = new TextBlock
                    {
                        Text = title + (canvas?.IsDirty == true ? " *" : ""), FontSize = 12,
                        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5, 0)
                    };
                    ToolTip.SetTip(label, canvas?.CurrentFilePath ?? title);
                    if (generated.Contains(tab))
                    {
                        var check = new CheckBox { IsChecked = _checkedSheets.Contains(canvas), MinWidth = 22, MinHeight = 28, Padding = new Thickness(0) };
                        AutomationProperties.SetName(check, "勾选 " + title);
                        check.IsCheckedChanged += (_, _) =>
                        {
                            if (check.IsChecked == true) _checkedSheets.Add(canvas); else _checkedSheets.Remove(canvas);
                            _scope = DrawingScope.Selected;
                            UpdateBatchActions();
                        };
                        row.Children.Add(check);
                    }
                    if (_documentBatches.TryGetValue(canvas, out var info) && info.Overview && !title.Contains("总览")) label.Text = "总览 · " + label.Text;
                    Grid.SetColumn(label, 1);
                    row.Children.Add(label);
                    var close = new Button
                    {
                        Name = "SidebarCloseDocument", Tag = tab, Content = "×", FontSize = 15,
                        Width = 26, Height = 26, MinHeight = 0, MinWidth = 0, Padding = new Thickness(0),
                        Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                        VerticalAlignment = VerticalAlignment.Center, IsEnabled = _docTabs.Items.Count > 1
                    };
                    ToolTip.SetTip(close, "关闭 " + title);
                    AutomationProperties.SetName(close, "关闭 " + title);
                    close.Click += async (_, e) => { e.Handled = true; await CloseSidebarDocumentAsync(tab); };
                    Grid.SetColumn(close, 2);
                    row.Children.Add(close);
                    _sidebarDocuments.Items.Add(new ListBoxItem
                    {
                        Classes = { "documentRow" }, Tag = tab, Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Padding = new Thickness(3, 2), Margin = new Thickness(0, 1), MinHeight = 36
                    });
                }
            }
            _sidebarDocuments.SelectedItem = _sidebarDocuments.Items.OfType<ListBoxItem>()
                .FirstOrDefault(row => ReferenceEquals(row.Tag, _docTabs.SelectedItem));
        }
        finally { _syncingSidebarDocuments = false; }
    }
}
