using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using lcdb.DrawingFrame;
namespace OtoCAD.Avalonia.Slim;

public static class SlimDialogs
{
    public static async Task Message(Window owner,string title,string text)
    {
        var w=new Window{Title=title,Width=620,Height=380,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var ok=new Button{Content="确定",IsDefault=true,Classes={"accent"},HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};ok.Click+=(_,_)=>w.Close();
        var grid=new Grid{RowDefinitions=new RowDefinitions("*,Auto"),Margin=new Thickness(20)};
        grid.Children.Add(new ScrollViewer{Content=new TextBlock{Text=text,TextWrapping=global::Avalonia.Media.TextWrapping.Wrap}});Grid.SetRow(ok,1);grid.Children.Add(ok);w.Content=grid;
        await w.ShowDialog(owner);
    }
    public static async Task<string?> Prompt(Window owner,string title,string initial,string hint="")
    {
        var w=new Window{Title=title,Width=540,Height=260,WindowStartupLocation=WindowStartupLocation.CenterOwner,CanResize=false};
        var box=new TextBox{Text=initial};var yes=new Button{Content="确定",IsDefault=true,Classes={"accent"}};var no=new Button{Content="取消",IsCancel=true};
        yes.Click+=(_,_)=>w.Close(box.Text);no.Click+=(_,_)=>w.Close(null);
        w.Content=new StackPanel{Margin=new Thickness(20),Spacing=12,Children={new TextBlock{Text=hint,TextWrapping=global::Avalonia.Media.TextWrapping.Wrap},box,new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8,Children={yes,no}}}};
        return await w.ShowDialog<string?>(owner);
    }
    public static async Task<string?> Library(Window owner, string dir, string current, string? startupDefault=null)
    {
        Directory.CreateDirectory(dir);
        var files = Directory.GetFiles(dir, "*.otocad").OrderBy(path => path).ToList();
        var window = new Window
        {
            Title = "模板库 · 选择后导入设计即可使用",
            Width = 820, Height = 560, MinWidth = 560, MinHeight = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var list = new ListBox { ItemsSource = files.Select(path => Path.GetFileNameWithoutExtension(path)
            + (string.Equals(path,current,StringComparison.OrdinalIgnoreCase) ? "  · 本次使用" : "")
            + (string.Equals(path,startupDefault,StringComparison.OrdinalIgnoreCase) ? "  · 启动默认" : "")).ToList() };
        var preview = new CadCanvas { Focusable = false, IsHitTestVisible = false, GridVisible = false, IsVisible = false };
        var previewMessage = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(12),
        };
        var previewCard = new Border
        {
            Classes = { "workbenchCard" }, ClipToBounds = true,
            Child = new Grid { Children = { preview, previewMessage } },
        };
        var yes = new Button { Content = "使用这个模板", IsDefault = true, Classes = { "accent" } };
        var no = new Button { Content = "取消", IsCancel = true };
        yes.Click += (_, _) =>
        {
            if (yes.IsEnabled && list.SelectedIndex >= 0)
                window.Close(files[list.SelectedIndex]);
        };
        no.Click += (_, _) => window.Close(null);

        void UpdatePreview()
        {
            preview.IsVisible = false;
            previewMessage.IsVisible = true;
            previewMessage.Text = "模板库暂无模板";
            yes.IsEnabled = false;
            if (list.SelectedIndex < 0) return;

            try
            {
                bool hasContent = preview.LoadOtocadFile(files[list.SelectedIndex]) > 0;
                previewMessage.Text = "此模板没有可预览的内容";
                previewMessage.IsVisible = !hasContent;
                preview.IsVisible = hasContent;
                yes.IsEnabled = hasContent;
            }
            catch (Exception)
            {
                previewMessage.Text = "无法读取此模板";
            }
        }

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("2*,14,3*"),
            Margin = new Thickness(20),
        };
        var hint = new TextBlock
        {
            Text = "点击模板查看右侧预览，确认后用于出图。",
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        Grid.SetColumnSpan(hint, 3);
        Grid.SetRow(list, 1);
        Grid.SetRow(previewCard, 1);
        Grid.SetColumn(previewCard, 2);
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Margin = new Thickness(0, 12, 0, 0), Children = { yes, no },
        };
        Grid.SetRow(bar, 2);
        Grid.SetColumnSpan(bar, 3);
        grid.Children.Add(hint);
        grid.Children.Add(list);
        grid.Children.Add(previewCard);
        grid.Children.Add(bar);
        window.Content = grid;

        list.SelectionChanged += (_, _) => UpdatePreview();
        if (files.Count == 0) UpdatePreview();
        else list.SelectedIndex = Math.Max(0, files.FindIndex(path => string.Equals(path,current,StringComparison.OrdinalIgnoreCase)));
        return await window.ShowDialog<string?>(owner);
    }
    internal static async Task<(DrawingScope Scope, string[] Formats)?> Export(
        Window owner, DrawingScopeOption[] options, DrawingScope preferred, string[] remembered)
    {
        var window = new Window { Title = "导出加工图", Width = 420, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var scope = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = options };
        scope.SelectedItem = options.FirstOrDefault(o => o.Scope == preferred)
            ?? options.FirstOrDefault(o => o.Count > 0);
        var formats = new[] { ("dxf", "DXF · CAD 交换文件"), ("pdf", "PDF · 矢量图纸"), ("png", "PNG · 高清预览图"),
            ("otocad", "OtoCAD · 可编辑图纸"), ("json", "JSON · 图纸数据") };
        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = "图纸范围" });
        panel.Children.Add(scope);
        panel.Children.Add(new TextBlock { Text = "文件格式", Margin = new Thickness(0, 8, 0, 0) });
        var checks = formats.Select(f => (f.Item1, Box: new CheckBox
            { Content = f.Item2, IsChecked = remembered.Contains(f.Item1, StringComparer.OrdinalIgnoreCase) })).ToList();
        var confirm = new Button { Content = "选择文件夹", IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = "取消", IsCancel = true };
        var summary = new TextBlock { FontSize = 12 };
        string[] Chosen() => checks.Where(c => c.Box.IsChecked == true).Select(c => c.Item1).ToArray();
        void Update()
        {
            var count = (scope.SelectedItem as DrawingScopeOption)?.Count ?? 0;
            int files = count * Chosen().Length;
            summary.Text = $"将导出 {count} 张加工图，共 {files} 个文件";
            confirm.IsEnabled = files > 0;
        }
        foreach (var check in checks)
        {
            check.Box.IsCheckedChanged += (_, _) => Update();
            panel.Children.Add(check.Box);
        }
        scope.SelectionChanged += (_, _) => Update();
        confirm.Click += (_, _) => { if (confirm.IsEnabled && scope.SelectedItem is DrawingScopeOption option) window.Close((option.Scope, Chosen())); };
        cancel.Click += (_, _) => window.Close(null);
        panel.Children.Add(summary);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { cancel, confirm } });
        window.Content = new ScrollViewer { Content = panel };
        Update();
        return await window.ShowDialog<(DrawingScope, string[])?>(owner);
    }

    internal static async Task<bool> ExportResult(Window owner, string dir, IReadOnlyList<string> lines, int failures, string summary)
    {
        var window = new Window { Title = "导出结果", Width = 560, Height = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var list = new ListBox { ItemsSource = lines };
        var retry = new Button { Content = $"重试失败项（{failures}）", IsVisible = failures > 0 };
        var open = new Button { Content = "打开文件夹" };
        var close = new Button { Content = "完成", IsDefault = true, IsCancel = true, Classes = { "accent" } };
        var path = new SelectableTextBlock { Text = dir, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        open.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true }); }
            catch (Exception ex) { path.Text = dir + "\\n" + ex.Message; }
        };
        retry.Click += (_, _) => window.Close(true);
        close.Click += (_, _) => window.Close(false);
        var grid = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
        grid.Children.Add(new TextBlock { Text = summary, Margin = new Thickness(0, 0, 0, 8) });
        Grid.SetRow(list, 1); grid.Children.Add(list);
        Grid.SetRow(path, 2); grid.Children.Add(path);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0), Children = { retry, open, close } };
        Grid.SetRow(buttons, 3); grid.Children.Add(buttons);
        window.Content = grid;
        return await window.ShowDialog<bool>(owner);
    }
}

public sealed class TableEditorDialog:Window
{
    readonly List<Action> _commit=new();
    void CommitEditors(){foreach(var action in _commit)action();}
    readonly TemplateTable _draft;readonly StackPanel _columns=new(){Orientation=Orientation.Horizontal,Spacing=10};TextBox? _focused;
    public TemplateTable? Result{get;private set;}
    public TableEditorDialog(TemplateTable source)
    {
        Title="自由表格 · 列数、行数和内容都可编辑";Width=920;Height=610;MinWidth=650;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        _draft=(TemplateTable)source.Clone();
        var grid=new Grid{RowDefinitions=new RowDefinitions("Auto,*,Auto"),Margin=new Thickness(16)};
        var fields=new ComboBox{ItemsSource=TemplatePipeline.Fields,SelectedIndex=0,Width=180};
        var insert=new Button{Content="插入设计参数"};insert.Click+=(_,_)=>{if(_focused is not null&&fields.SelectedItem is string key)InsertParameter(_focused,key);};
        var addC=new Button{Content="＋ 列"};addC.Click+=(_,_)=>{CommitEditors();_draft.Columns.Add(new IsoSpecColumn{Title="新列",Rows=Enumerable.Repeat("",Math.Max(1,_draft.Columns.Max(c=>c.Rows.Count))).ToList()});_draft.ColumnWidths.Add(1);Rebuild();};
        var addR=new Button{Content="＋ 行"};addR.Click+=(_,_)=>{CommitEditors();foreach(var c in _draft.Columns)c.Rows.Add("");Rebuild();};
        var delR=new Button{Content="－ 最后一行"};delR.Click+=(_,_)=>{CommitEditors();foreach(var c in _draft.Columns)if(c.Rows.Count>1)c.Rows.RemoveAt(c.Rows.Count-1);Rebuild();};
        var top=new StackPanel{Spacing=8,Margin=new Thickness(0,0,0,12),Children={new TextBlock{Text="点击要编辑的格子；需要自动填数时，选择参数后点击“插入设计参数”。"},new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Children={addC,addR,delR,fields,insert}}}};grid.Children.Add(top);
        var scroll=new ScrollViewer{Content=_columns,HorizontalScrollBarVisibility=global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);grid.Children.Add(scroll);
        var yes=new Button{Content="应用表格",IsDefault=true,Classes={"accent"}};yes.Click+=(_,_)=>{CommitEditors();Result=_draft;Close(true);};var no=new Button{Content="取消",IsCancel=true};no.Click+=(_,_)=>Close(false);
        var bar=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8,Margin=new Thickness(0,12,0,0),Children={yes,no}};Grid.SetRow(bar,2);grid.Children.Add(bar);Content=grid;Rebuild();
    }
    internal static void InsertParameter(TextBox editor,string key)
    {
        string text=editor.Text??"";
        int start=Math.Clamp(Math.Min(editor.SelectionStart,editor.SelectionEnd),0,text.Length);
        int end=Math.Clamp(Math.Max(editor.SelectionStart,editor.SelectionEnd),start,text.Length);
        string token=TemplatePipeline.Token(key);
        editor.Text=text[..start]+token+text[end..];
        editor.Focus();
        editor.CaretIndex=start+token.Length;
        editor.SelectionStart=editor.SelectionEnd=editor.CaretIndex;
    }
    void Rebuild()
    {
        _columns.Children.Clear();_commit.Clear();_focused=null;int rowCount=Math.Max(1,_draft.Columns.Max(c=>c.Rows.Count));
        for(int c=0;c<_draft.Columns.Count;c++)
        {
            int ci=c;var col=_draft.Columns[c];while(col.Rows.Count<rowCount)col.Rows.Add("");while(_draft.ColumnWidths.Count<=c)_draft.ColumnWidths.Add(1);
            var stack=new StackPanel{Width=210,Spacing=5};
            var title=new TextBox{Text=col.Title,Watermark="列标题"};_commit.Add(()=>col.Title=title.Text??"");title.GotFocus+=(_,_)=>_focused=title;stack.Children.Add(title);
            var width=new NumericUpDown{Value=(decimal)_draft.ColumnWidths[c],Minimum=.1m,Increment=.1m,FormatString="0.0"};_commit.Add(()=>{if(width.Value.HasValue)_draft.ColumnWidths[ci]=(double)width.Value.Value;});
            stack.Children.Add(new TextBlock{Text="列宽比例"});stack.Children.Add(width);
            for(int r=0;r<rowCount;r++)
            {
                int ri=r;var cell=new TextBox{Text=col.Rows[r],Watermark=$"第{r+1}行"};_commit.Add(()=>col.Rows[ri]=cell.Text??"");cell.GotFocus+=(_,_)=>_focused=cell;stack.Children.Add(cell);
            }
            var remove=new Button{Content="删除这一列"};remove.Click+=(_,_)=>{CommitEditors();if(_draft.Columns.Count>1){_draft.Columns.RemoveAt(ci);_draft.ColumnWidths.RemoveAt(ci);Rebuild();}};stack.Children.Add(remove);_columns.Children.Add(stack);
        }
    }
}
