using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using lcdb;
using lcdb.Optic;
using lcdb.Optic.Import;
using OtoCAD.Avalonia.Slim;

namespace OtoCAD.Avalonia.Dialogs;

public partial class ImportSelectionDialog : Window
{
    private sealed class ElementRow
    {
        public required Entity Part { get; init; }
        public required int Number { get; init; }
        public required bool SharedAperture { get; init; }
        public CheckBox Select { get; } = new() { IsChecked = true, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        public TextBlock Label { get; } = new() { TextWrapping = TextWrapping.Wrap };
        public TextBox Diameter { get; } = new() { MinWidth = 65 };
        public TextBox Mechanical { get; } = new() { MinWidth = 65, Watermark = "同轮廓口径" };
        public CheckBox Confirm { get; } = new() { Content = "确认为实际通光口径", IsChecked = false };
        public TextBlock Error { get; } = new() { Foreground = Brushes.DarkRed, TextWrapping = TextWrapping.Wrap };
        public bool IsValid => string.IsNullOrEmpty(Error.Text);
    }

    private readonly List<ElementRow> _rows = new();
    private bool _bulkUpdate;
    public List<Entity>? Result { get; private set; }
    public bool MakeLayoutTab { get; private set; }
    public bool MakePerElementTabs => true;

    public ImportSelectionDialog() => AvaloniaXamlLoader.Load(this);

    public ImportSelectionDialog(IReadOnlyList<Entity> entities, string sourceSummary, string? partialDetails = null) : this()
    {
        this.FindControl<TextBlock>("SummaryText")!.Text = sourceSummary;
        if (partialDetails is not null)
        {
            this.FindControl<TextBlock>("PreviewTitle")!.Text = "可出图元件预览（部分光路）";
            this.FindControl<Expander>("ImportDetails")!.IsVisible = true;
            this.FindControl<SelectableTextBlock>("ImportDetailsText")!.Text = partialDetails;
            this.FindControl<CheckBox>("LayoutTabCheck")!.Content = "另生成部分光路总览（仅展示所选可出图元件）";
            SizeChanged += (_, e) => this.FindControl<ScrollViewer>("DetailsScroll")!.MaxHeight = Math.Clamp(e.NewSize.Height * .17, 48, 110);
        }
        var panel = this.FindControl<StackPanel>("ItemsPanel")!;
        foreach (var part in entities)
            AddRow(panel, part, ImportIdentity.ElementNumber(part, _rows.Count + 1));

        this.FindControl<Button>("SelectAllBtn")!.Click += (_, _) => SetAll(true);
        this.FindControl<Button>("SelectNoneBtn")!.Click += (_, _) => SetAll(false);
        this.FindControl<Button>("OkBtn")!.Click += (_, _) =>
        {
            var selected = _rows.Where(row => row.Select.IsChecked == true).ToList();
            if (selected.Count == 0 || selected.Any(row => !row.IsValid)) return;
            Result = selected.Select(row => row.Part).ToList();
            MakeLayoutTab = this.FindControl<CheckBox>("LayoutTabCheck")!.IsChecked == true;
            Close();
        };
        this.FindControl<Button>("CancelBtn")!.Click += (_, _) => Close();
        SizeChanged += (_, e) => ArrangeBody(e.NewSize.Width < 720);
        Opened += (_, _) =>
        {
            var screen = Screens.ScreenFromWindow(this);
            if (screen is not null)
            {
                Width = Math.Min(Width, Math.Max(MinWidth, screen.WorkingArea.Width / screen.Scaling - 16));
                Height = Math.Min(Height, Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - 48));
            }
            ArrangeBody(ClientSize.Width < 720);
            this.FindControl<CadCanvas>("PreviewCanvas")!.ResetView();
        };
        RefreshPreview();
    }

    private void ArrangeBody(bool narrow)
    {
        var body = this.FindControl<Grid>("Body")!;
        var selection = this.FindControl<Border>("SelectionPanel")!;
        if (body.RowDefinitions.Count == (narrow ? 2 : 1)) return;
        body.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,*");
        body.RowDefinitions = new RowDefinitions(narrow ? "1*,3*" : "*");
        selection.Margin = narrow ? new Thickness(0,4,0,0) : new Thickness(4,0,0,0);
        Grid.SetColumn(selection, narrow ? 0 : 1);
        Grid.SetRow(selection, narrow ? 1 : 0);
    }

    private static OpticalSurface[] Surfaces(Entity part) => part switch
    {
        OpticalLens s => new[] { s.FrontSurface, s.BackSurface },
        CementedLens d => new[] { d.FrontSurface, d.ContactSurface, d.BackSurface },
        _ => throw new NotSupportedException("仅支持单透镜和双胶合透镜。")
    };

    private static string Number(double value) =>
        double.IsInfinity(value) ? "∞" : value.ToString("R", CultureInfo.InvariantCulture);

    private void AddRow(StackPanel panel, Entity part, int index)
    {
        bool missing = Surfaces(part).All(surface => surface.SemiAperture <= 0);
        bool suggested = missing && TemplatePipeline.TrySetIllustrativeAperture(part);
        var row = new ElementRow { Part = part, Number = index, SharedAperture = missing || TemplatePipeline.IsIllustrative(part) };
        _rows.Add(row);
        var single = part as OpticalLens;
        var doublet = part as CementedLens;
        row.Diameter.Text = missing && !suggested ? "" : Number(single?.Diameter ?? doublet!.Diameter);
        var mechanical = single?.MechanicalDiameter ?? doublet?.MechanicalDiameter;
        row.Mechanical.Text = mechanical is double od ? Number(od) : "";
        row.Confirm.IsVisible = row.SharedAperture;
        row.Select.Content = row.Label;
        var fields = new Grid { ColumnDefinitions = new("*,*") };
        AddField(fields, 0, "轮廓通光口径 (mm)", row.Diameter);
        AddField(fields, 1, "机械外径 (mm)", row.Mechanical);
        panel.Children.Add(new StackPanel { Spacing = 3, Margin = new Thickness(2,2,2,6),
            Children = { row.Select, fields, row.Confirm, row.Error } });
        row.Select.IsCheckedChanged += (_, _) => { if (!_bulkUpdate) RefreshPreview(); };
        row.Diameter.PropertyChanged += (_, change) =>
        {
            if (change.Property != TextBox.TextProperty) return;
            if (row.Confirm.IsChecked == true) row.Confirm.IsChecked = false;
            else UpdateRow(row, true);
        };
        row.Mechanical.PropertyChanged += (_, change) => { if (change.Property == TextBox.TextProperty) UpdateRow(row, true); };
        row.Confirm.IsCheckedChanged += (_, _) => UpdateRow(row, true);
        UpdateRow(row, false);
    }

    private static void AddField(Grid grid, int column, string label, TextBox box)
    {
        var field = new StackPanel { Margin = column == 1 ? new Thickness(4,0,0,0) : default, Children = { new TextBlock { Text = label, FontSize = 12, TextWrapping = TextWrapping.Wrap }, box } };
        Grid.SetColumn(field, column);
        grid.Children.Add(field);
        global::Avalonia.Automation.AutomationProperties.SetName(box, label);
    }

    private static double? ReadDiameter(string? input)
    {
        bool valid = double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            || double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return valid && double.IsFinite(value) && value > 0 && value <= (double)decimal.MaxValue ? value : null;
    }

    private void UpdateRow(ElementRow row, bool redraw)
    {
        var single = row.Part as OpticalLens;
        var doublet = row.Part as CementedLens;
        double? diameter = ReadDiameter(row.Diameter.Text);
        bool hasMechanical = !string.IsNullOrWhiteSpace(row.Mechanical.Text);
        double? mechanical = ReadDiameter(row.Mechanical.Text);
        string error = "";
        if (!diameter.HasValue) error = "通光口径须为正数。";
        else if (hasMechanical && !mechanical.HasValue) error = "机械外径须为正数，留空表示与轮廓口径相同。";
        else if (mechanical < diameter) error = "机械外径小于通光口径，请修正。";
        else
        {
            var surfaces = Surfaces(row.Part);
            if (single is not null) { single.Diameter = diameter.Value; single.MechanicalDiameter = mechanical; }
            else { doublet!.Diameter = diameter.Value; doublet.MechanicalDiameter = mechanical; }
            if (row.SharedAperture)
            {
                foreach (var surface in surfaces) surface.SemiAperture = diameter.Value / 2;
                var ext = single is not null ? (single.Extensions ??= new()) : (doublet!.Extensions ??= new());
                bool confirmed = row.Confirm.IsChecked == true;
                ext.Tags["apertureSource"] = confirmed ? "user-confirmed" : "illustrative";
                ext.Tags["apertureNote"] = confirmed ? "用户已确认实际通光口径" : "文件未提供口径，采用示意值";
            }
            row.Part.InvalidateRenderCache();
            try { TemplatePipeline.ValidatePart(row.Part); }
            catch (InvalidOperationException ex) { error = ex.Message; }
        }
        row.Error.Text = error;
        row.Error.IsVisible = error.Length > 0;
        string label = single is not null
            ? $"单透镜 {single.MaterialName}  R1={Number(single.R1)}  R2={Number(single.R2)}  d={Number(single.Thickness)}"
            : $"双胶合 {doublet!.Material1}+{doublet.Material2}  R1={Number(doublet.R1)}  Rc={Number(doublet.RContact)}  R3={Number(doublet.R3)}";
        string status = error.Length > 0 ? "待修正" : TemplatePipeline.IsIllustrative(row.Part) ? "示意" : row.SharedAperture ? "已确认" : "处方";
        row.Label.Text = $"#{row.Number}  {label}  {ImportIdentity.Tag(row.Part, "sourceSurfaces")}\n" +
            (error.Length == 0 ? $"通光 Ø{Number(diameter!.Value)}  ·  外径 Ø{Number(mechanical ?? diameter.Value)} mm  ·  {status}" : status);
        if (redraw && !_bulkUpdate) RefreshPreview();
    }

    private void SetAll(bool selected)
    {
        _bulkUpdate = true;
        try { foreach (var row in _rows) row.Select.IsChecked = selected; }
        finally { _bulkUpdate = false; }
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        var selected = _rows.Where(row => row.Select.IsChecked == true).ToList();
        int invalid = selected.Count(row => !row.IsValid);
        this.FindControl<TextBlock>("CountText")!.Text = $"已选 {selected.Count} / {_rows.Count}";
        this.FindControl<Button>("OkBtn")!.IsEnabled = selected.Count > 0 && invalid == 0;
        this.FindControl<TextBlock>("ValidationText")!.Text = invalid > 0 ? $"{invalid} 个勾选元件需修正" : "";
        var preview = this.FindControl<CadCanvas>("PreviewCanvas")!;
        var overlay = this.FindControl<TextBlock>("PreviewMessage")!;
        try
        {
            var valid = selected.Where(row => row.IsValid).Select(row => (Entity)row.Part.Clone()).ToList();
            preview.LoadGeneratedSheet(valid);
            overlay.Text = selected.Count == 0 ? "勾选元件以预览" : invalid > 0 ? "待修正元件暂未显示" : "";
        }
        catch (Exception ex)
        {
            preview.LoadGeneratedSheet(Array.Empty<Entity>());
            overlay.Text = "预览失败：" + ex.Message;
        }
    }
}
