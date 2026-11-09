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

public partial class MainWindow
{
    private ListBox? _sidebarDocuments;
    private Control? _sidebarDocumentPanel;
    private Control? _sidebarProperties;
    private bool _syncingSidebarDocuments;
    private bool _clearingDrawings;
    private readonly Dictionary<TabItem, string> _sidebarDocumentNames = new();
    private readonly Dictionary<string, Button> _sidebarPageButtons = new();
    public string ActiveSidebarPage { get; private set; } = "出图";
    public IReadOnlyList<TabItem> SidebarDocumentTabs => _docTabs.Items.OfType<TabItem>().ToArray();

    private void InitializeSidebarWorkspace()
    {
        var workspace = this.FindControl<Grid>("WorkspaceGrid")!;
        workspace.SizeChanged += (_, e) =>
        {
            double width = Math.Clamp(Math.Round(e.NewSize.Width * 0.24), 280, 370);
            workspace.ColumnDefinitions[0].Width = new GridLength(width);
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
        WireFileAction("SidebarOpenDocument", "File.Open", "open");
        WireFileAction("SidebarSaveDocument", "File.Save", "save");
        WireFileAction("SidebarSaveAsDocument", "File.SaveAs", "save-as");
        var clear = this.FindControl<Button>("SidebarClearDrawings")!;
        clear.Click += async (_, _) => await ClearGeneratedDrawingsAsync();
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
        if (page == "图纸") RefreshSidebarDocuments();
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

    private List<TabItem> GeneratedDrawingTabs() => _docTabs.Items.OfType<TabItem>()
        .Where(tab => tab.Content is CadCanvas canvas && !ReferenceEquals(canvas, _templateCanvas)
            && Slim.TemplatePipeline.IsGeneratedDrawing(canvas.GetAllEntities())).ToList();

    /// <summary>Close generated sheets as one batch; never remove templates or saved files.</summary>
    public async Task<int> ClearGeneratedDrawingsAsync(Func<int,int,Task<bool>>? confirmDiscard = null)
    {
        if (_clearingDrawings) return 0;
        var targets = GeneratedDrawingTabs();
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
            var survivor = _docTabs.Items.OfType<TabItem>().FirstOrDefault(tab => ReferenceEquals(tab.Content,_templateCanvas))
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
                if (tab.Content is CadCanvas canvas) _sheets.Remove(canvas);
                _docTabs.Items.Remove(tab); _sidebarDocumentNames.Remove(tab);
                (tab.Content as IDisposable)?.Dispose();
            }
            RefreshSidebarDocuments(); SelectSidebarPage("图纸");
            _status.Text = $"已清空 {targets.Count} 张加工图，模板已保留；可以导入新的镜头。";
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

    private void RefreshSidebarDocuments()
    {
        if (_sidebarDocuments is null) return;
        var clear = this.FindControl<Button>("SidebarClearDrawings");
        if (clear is not null) { int count = GeneratedDrawingTabs().Count; clear.Content = count > 0 ? $"清空加工图（{count}）" : "清空加工图"; clear.IsEnabled = count > 0 && !_clearingDrawings; }
        _syncingSidebarDocuments = true;
        try
        {
            _sidebarDocuments.Items.Clear();
            foreach (var tab in _docTabs.Items.OfType<TabItem>())
            {
                var canvas = tab.Content as CadCanvas;
                string title = canvas?.CurrentFilePath is string path
                    ? System.IO.Path.GetFileNameWithoutExtension(path)
                    : _sidebarDocumentNames.TryGetValue(tab, out string? stored) ? stored : "图纸";
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 32 };
                var label = new TextBlock
                {
                    Text = title + (canvas?.IsDirty == true ? " *" : ""), FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0)
                };
                ToolTip.SetTip(label, canvas?.CurrentFilePath ?? title);
                row.Children.Add(label);
                var close = new Button
                {
                    Name = "SidebarCloseDocument", Tag = tab, Content = "×", FontSize = 15,
                    Width = 26, Height = 26, MinHeight = 0, MinWidth = 0, Padding = new Thickness(0),
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center, IsEnabled = _docTabs.Items.Count > 1
                };
                ToolTip.SetTip(close, "关闭这张图纸");
                close.Click += async (_, e) => { e.Handled = true; await CloseSidebarDocumentAsync(tab); };
                Grid.SetColumn(close, 1);
                row.Children.Add(close);
                _sidebarDocuments.Items.Add(new ListBoxItem
                {
                    Classes = { "documentRow" }, Tag = tab, Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(3, 2), Margin = new Thickness(0, 1), MinHeight = 36
                });
            }
            _sidebarDocuments.SelectedItem = _sidebarDocuments.Items.OfType<ListBoxItem>()
                .FirstOrDefault(row => ReferenceEquals(row.Tag, _docTabs.SelectedItem));
        }
        finally { _syncingSidebarDocuments = false; }
    }
}
