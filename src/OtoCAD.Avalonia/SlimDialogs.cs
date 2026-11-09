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
    public static async Task<string?> Library(Window owner, string dir, string current)
    {
        Directory.CreateDirectory(dir);
        var files = Directory.GetFiles(dir, "*.otocad").OrderBy(path => path).ToList();
        var window = new Window
        {
            Title = "模板库 · 选择后导入设计即可使用",
            Width = 820, Height = 560, MinWidth = 560, MinHeight = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var list = new ListBox { ItemsSource = files.Select(Path.GetFileNameWithoutExtension).ToList() };
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
                yes.IsEnabled = true;
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
        else list.SelectedIndex = Math.Max(0, files.FindIndex(path => path == current));
        return await window.ShowDialog<string?>(owner);
    }
    public static async Task<(bool All,List<string> Formats)?> Export(Window owner,int count)
    {
        var w=new Window{Title="导出图纸",Width=600,Height=410,WindowStartupLocation=WindowStartupLocation.CenterOwner,CanResize=false};
        var all=new CheckBox{Content=$"导出全部已生成加工图（{count} 张）；不勾选则只导出当前图纸",IsChecked=count>0};
        var formats=new[]{("dxf","DXF · CAD 交换文件"),("pdf","PDF · 矢量图纸"),("png","PNG · 高清预览图"),("otocad","OtoCAD · 原生可编辑图纸"),("json","JSON · 原生图纸数据")};
        var checks=formats.Select(x=>(x.Item1,Box:new CheckBox{Content=x.Item2,IsChecked=true})).ToList();
        var stack=new StackPanel{Margin=new Thickness(20),Spacing=8,Children={all,new TextBlock{Text="文件格式",FontWeight=global::Avalonia.Media.FontWeight.Bold,Margin=new Thickness(0,8,0,0)}}};
        foreach(var c in checks)stack.Children.Add(c.Box);
        var yes=new Button{Content="选择文件夹并导出",IsDefault=true,Classes={"accent"}};var no=new Button{Content="取消",IsCancel=true};
        yes.Click+=(_,_)=>{var chosen=checks.Where(x=>x.Box.IsChecked==true).Select(x=>x.Item1).ToList();if(chosen.Count>0)w.Close((all.IsChecked==true,chosen));};no.Click+=(_,_)=>w.Close(null);
        stack.Children.Add(new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0),Children={yes,no}});w.Content=stack;
        return await w.ShowDialog<(bool,List<string>)?>(owner);
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
        var insert=new Button{Content="插入设计参数"};insert.Click+=(_,_)=>{if(_focused is not null&&fields.SelectedItem is string key)_focused.Text=(_focused.Text??"")+TemplatePipeline.Token(key);};
        var addC=new Button{Content="＋ 列"};addC.Click+=(_,_)=>{CommitEditors();_draft.Columns.Add(new IsoSpecColumn{Title="新列",Rows=Enumerable.Repeat("",Math.Max(1,_draft.Columns.Max(c=>c.Rows.Count))).ToList()});_draft.ColumnWidths.Add(1);Rebuild();};
        var addR=new Button{Content="＋ 行"};addR.Click+=(_,_)=>{CommitEditors();foreach(var c in _draft.Columns)c.Rows.Add("");Rebuild();};
        var delR=new Button{Content="－ 最后一行"};delR.Click+=(_,_)=>{CommitEditors();foreach(var c in _draft.Columns)if(c.Rows.Count>1)c.Rows.RemoveAt(c.Rows.Count-1);Rebuild();};
        var top=new StackPanel{Spacing=8,Margin=new Thickness(0,0,0,12),Children={new TextBlock{Text="点击要编辑的格子；需要自动填数时，选择参数后点击“插入设计参数”。"},new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Children={addC,addR,delR,fields,insert}}}};grid.Children.Add(top);
        var scroll=new ScrollViewer{Content=_columns,HorizontalScrollBarVisibility=global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);grid.Children.Add(scroll);
        var yes=new Button{Content="应用表格",IsDefault=true,Classes={"accent"}};yes.Click+=(_,_)=>{CommitEditors();Result=_draft;Close(true);};var no=new Button{Content="取消",IsCancel=true};no.Click+=(_,_)=>Close(false);
        var bar=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8,Margin=new Thickness(0,12,0,0),Children={yes,no}};Grid.SetRow(bar,2);grid.Children.Add(bar);Content=grid;Rebuild();
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
