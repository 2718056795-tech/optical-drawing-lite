using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using lcdb;
using lcdb.Optic;

namespace OtoCAD.Avalonia.Dialogs;

/// <summary>
/// 导入光学设计的预览+选择对话框: 左侧光路图实时预览(勾选即所见), 右侧逐元件勾选, 底部生成选项.
/// 关闭后 <see cref="Result"/> = 选中实体; null = 取消. <see cref="MakeLayoutTab"/>/<see cref="MakePerElementTabs"/> = 生成选项.
/// </summary>
public partial class ImportSelectionDialog : Window
{
    private readonly List<(CheckBox cb, Entity e)> _rows = new();
    private CadCanvas? _preview;
    private readonly Dictionary<Entity,NumericUpDown> _requiredApertures=new();

    /// <summary>选中的实体 (按原顺序); null = 取消.</summary>
    public List<Entity>? Result { get; private set; }
    /// <summary>是否生成「光路图总览」tab.</summary>
    public bool MakeLayoutTab { get; private set; } = true;
    /// <summary>是否为每个选中元件生成「加工图」tab.</summary>
    public bool MakePerElementTabs { get; private set; } = true;

    public ImportSelectionDialog()
    {
        InitializeComponent();
    }

    public ImportSelectionDialog(IReadOnlyList<Entity> entities, string sourceSummary) : this()
    {
        _preview = this.FindControl<CadCanvas>("PreviewCanvas");
        this.FindControl<TextBlock>("SummaryText")!.Text = sourceSummary;

        var panel = this.FindControl<StackPanel>("ItemsPanel")!;
        int i = 1;
        foreach (var e in entities)
        {
            Slim.TemplatePipeline.TrySetIllustrativeAperture(e);
            var cb = new CheckBox
            {
                Content = Describe(i, e),
                IsChecked = true,
                Margin = new Thickness(0, 2, 0, 2),
            };
            cb.IsCheckedChanged += (_, _) => RefreshPreview();
            var row=new StackPanel{Spacing=3,Margin=new Thickness(0,0,0,9)};
            cb.Content=new TextBlock{Text=Describe(i,e),TextWrapping=global::Avalonia.Media.TextWrapping.Wrap,MaxWidth=400};row.Children.Add(cb);
            double dia=e is OpticalLens ol?ol.Diameter:e is CementedLens dl?dl.Diameter:25.4;
            var edit=new NumericUpDown{Value=(decimal)dia,Minimum=.01m,Maximum=10000,Increment=.1m,FormatString="0.###",Width=140};
            edit.ValueChanged+=(_,ev)=>{if(ev.NewValue.HasValue){double v=(double)ev.NewValue.Value;if(e is OpticalLens a){a.Diameter=v;if(Slim.TemplatePipeline.IsIllustrative(e)){a.FrontSurface.SemiAperture=v/2;a.BackSurface.SemiAperture=v/2;}}if(e is CementedLens b){b.Diameter=v;if(Slim.TemplatePipeline.IsIllustrative(e)){b.FrontSurface.SemiAperture=v/2;b.ContactSurface.SemiAperture=v/2;b.BackSurface.SemiAperture=v/2;}}RefreshPreview();}};
            bool missing=e is OpticalLens aa?aa.FrontSurface.SemiAperture<=0&&aa.BackSurface.SemiAperture<=0:e is CementedLens bb&&bb.FrontSurface.SemiAperture<=0&&bb.BackSurface.SemiAperture<=0;
            if(missing){edit.Value=null;_requiredApertures[e]=edit;}
            row.Children.Add(new StackPanel{Orientation=global::Avalonia.Layout.Orientation.Horizontal,Spacing=8,Children={new TextBlock{Text=Slim.TemplatePipeline.IsIllustrative(e)?"示意口径（可改）":missing?"缺少口径，请补填 (mm)":"轮廓口径 (mm)",VerticalAlignment=global::Avalonia.Layout.VerticalAlignment.Center},edit}});
            var mech=new NumericUpDown{Value=e is OpticalLens ml?(decimal?)ml.MechanicalDiameter:e is CementedLens md?(decimal?)md.MechanicalDiameter:null,Minimum=.01m,Maximum=10000,Increment=.1m,FormatString="0.###",Width=140};
            mech.ValueChanged+=(_,ev)=>{double? v=ev.NewValue.HasValue?(double)ev.NewValue.Value:null;if(e is OpticalLens a)a.MechanicalDiameter=v;if(e is CementedLens b)b.MechanicalDiameter=v;RefreshPreview();};
            row.Children.Add(new StackPanel{Orientation=global::Avalonia.Layout.Orientation.Horizontal,Spacing=8,Children={new TextBlock{Text="机械外径 (可空)",VerticalAlignment=global::Avalonia.Layout.VerticalAlignment.Center},mech}});
            panel.Children.Add(row);
            _rows.Add((cb, e));
            i++;
        }

        this.FindControl<Button>("SelectAllBtn")!.Click += (_, _) => SetAll(true);
        this.FindControl<Button>("SelectNoneBtn")!.Click += (_, _) => SetAll(false);
        this.FindControl<Button>("OkBtn")!.Click += (_, _) =>
        {
            var selected=_rows.Where(r=>r.cb.IsChecked==true).Select(r=>r.e).ToList();
            if(selected.Any(e=>_requiredApertures.TryGetValue(e,out var input)&&(!input.Value.HasValue||input.Value<=0))){this.FindControl<TextBlock>("SummaryText")!.Text="请先补填勾选元件的实际口径；缺失值不能用入瞳或默认值代替。";return;}
            Result = selected;
            MakeLayoutTab = this.FindControl<CheckBox>("LayoutTabCheck")!.IsChecked == true;
            MakePerElementTabs = this.FindControl<CheckBox>("PerElementCheck")!.IsChecked == true;
            Close();
        };
        this.FindControl<Button>("CancelBtn")!.Click += (_, _) =>
        {
            Result = null;
            Close();
        };

        RefreshPreview();
        // 布局完成后再刷一次, 确保画布有尺寸时能正确适配视图.
        Opened += (_, _) => RefreshPreview();
    }

    private void SetAll(bool value)
    {
        foreach (var (cb, _) in _rows) cb.IsChecked = value;  // 触发各自 RefreshPreview
    }

    /// <summary>把当前勾选的元件 (克隆) 灌进预览画布, 适配视图. 勾选变化即刷新.</summary>
    private void RefreshPreview()
    {
        if (_preview is null) return;
        try
        {
            var clones = _rows.Where(r => r.cb.IsChecked == true)
                              .Select(r => (Entity)r.e.Clone())
                              .ToList();
            _preview.LoadGeneratedSheet(clones);
        }
        catch { /* 预览失败不阻断导入 */ }
    }

    private static string Describe(int index, Entity e)
    {
        string tag = SurfaceTag(e)+(Slim.TemplatePipeline.IsIllustrative(e)?" 【示意口径】":"");
        switch (e)
        {
            case OpticalLens l:
                return $"#{index}  单透镜  {l.MaterialName}   R1={R(l.R1)} R2={R(l.R2)}   d={l.Thickness:F2}   净Ø{l.Diameter:F1}{tag}";
            case CementedLens d:
                return $"#{index}  双胶合  {d.Material1}+{d.Material2}   R1={R(d.R1)} Rc={R(d.RContact)} R3={R(d.R3)}   净Ø{d.Diameter:F1}{tag}";
            default:
                return $"#{index}  {e.className}";
        }
    }

    private static string SurfaceTag(Entity e)
    {
        OpticalExtensions? ext = (e as OpticalLens)?.Extensions ?? (e as CementedLens)?.Extensions;
        if (ext != null && ext.Tags.TryGetValue("surfaceIndex", out var s))
            return $"   (面 S{s})";
        return "";
    }

    private static string R(double r)
        => double.IsInfinity(r) ? "∞" : r.ToString("F2", CultureInfo.InvariantCulture);

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
