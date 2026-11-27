using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using lcdb;
using System.Globalization;

namespace OtoCAD.Avalonia.PropertyPanel;

public partial class PropertyPanelControl
{
    internal Func<DimensionBase, Func<double?, BatchPropertyResult>?>? BatchForDimensionHeight { get; set; }
    internal Action<DimensionBase, double?>? CommitDimensionHeight { get; set; }

    private Control MakeDimensionTextHeightEditor(DimensionBase dim, Action changed)
    {
        int generation = _editorGeneration;
        var panel = new StackPanel { Spacing = 5, Margin = new Thickness(4, 5) };
        panel.Children.Add(new TextBlock { Text = "标注字号（纸面 mm）", FontWeight = FontWeight.SemiBold });
        const string inherit = "跟随标注样式";
        string Format(double h) => h.ToString("G12", CultureInfo.InvariantCulture);
        var sizes = new[] { inherit, "1.8", "2.5", "3.5", "5", "7", "10" };
        var preset = new ComboBox { Name = "DimensionTextHeightPreset", ItemsSource = sizes,
            PlaceholderText = "常用字号", HorizontalAlignment = HorizontalAlignment.Stretch };
        var input = new TextBox { Name = "DimensionTextHeightInput", Text = Format(dim.EffectiveTextHeight),
            Watermark = "输入纸面字高" };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        row.Children.Add(preset);
        input.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(input, 1);
        row.Children.Add(input);
        panel.Children.Add(row);
        var feedback = new TextBlock { Name = "DimensionTextHeightFeedback", FontSize = 11, TextWrapping = TextWrapping.Wrap };
        bool follow = dim.TextHeightOverride is null;
        bool syncing = false;
        string? displayed = input.Text;

        void ShowValue(double? h)
        {
            syncing = true;
            follow = h is null;
            string value = Format(h ?? dim.style.TextHeight);
            preset.SelectedItem = follow ? inherit : sizes.Contains(value) ? value : null;
            displayed = value;
            input.Text = value;
            syncing = false;
        }
        bool Read(out double? desired)
        {
            desired = null;
            if (follow) return true;
            if (double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && double.IsFinite(value) && value > 0 && float.IsFinite((float)value))
            { desired = value; return true; }
            return false;
        }
        void CancelDraft()
        {
            PreviewEdit?.Invoke(dim, null);
            ShowValue(dim.TextHeightOverride);
            feedback.Text = "";
        }
        void Preview()
        {
            if (generation != _editorGeneration) return;
            feedback.Text = "";
            bool valid = Read(out var h);
            if (valid && h == dim.TextHeightOverride) ReleasePropertyDraft(CancelDraft);
            else ActivatePropertyDraft(CancelDraft);
            PreviewEdit?.Invoke(dim, valid && h != dim.TextHeightOverride
                ? copy => ((DimensionBase)copy).TextHeightOverride = h : null);
        }
        preset.SelectionChanged += (_, _) =>
        {
            if (syncing || preset.SelectedItem is not string selected) return;
            ShowValue(selected == inherit ? null : double.Parse(selected, CultureInfo.InvariantCulture));
            Preview();
        };
        input.TextChanged += (_, _) =>
        {
            if (syncing || input.Text == displayed) return;
            displayed = input.Text;
            syncing = true;
            follow = false;
            preset.SelectedItem = sizes.Skip(1).Contains(input.Text) ? input.Text : null;
            syncing = false;
            Preview();
        };
        var batch = BatchForDimensionHeight?.Invoke(dim);
        void Apply(bool all)
        {
            if (generation != _editorGeneration) return;
            if (!Read(out var h))
            {
                feedback.Foreground = Brushes.Firebrick;
                feedback.Text = "请输入大于零的有效字高，单位为 mm。";
                return;
            }
            PreviewEdit?.Invoke(dim, null);
            ReleasePropertyDraft(CancelDraft);
            feedback.Foreground = Foreground;
            if (all && batch is not null)
            {
                var result = batch(h);
                feedback.Text = result.Message;
                if (result.SourceAccepted) ShowValue(dim.TextHeightOverride);
            }
            else
            {
                if (dim.TextHeightOverride != h)
                {
                    if (CommitDimensionHeight is { } commit) commit(dim, h);
                    else { dim.TextHeightOverride = h; dim.Generate(); changed(); }
                }
                ShowValue(dim.TextHeightOverride);
                feedback.Text = "";
            }
        }
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        var apply = new Button { Name = "DimensionTextHeightApply", Content = "应用字号", Classes = { "accent" }, Margin = new Thickness(0,0,5,0) };
        apply.Click += (_, _) => Apply(false);
        buttons.Children.Add(apply);
        var reset = new Button { Name = "DimensionTextHeightReset", Content = "恢复默认" };
        reset.Click += (_, _) => { ShowValue(null); Apply(false); };
        buttons.Children.Add(reset);
        input.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) { Apply(false); e.Handled = true; }
            else if (e.Key == Key.Escape && CancelPropertyDraft()) e.Handled = true;
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        panel.Children.Add(buttons);
        if (batch is not null)
        {
            var all = new Button { Name = "DimensionTextHeightApplyAll", Content = "应用到所有图纸",
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            all.Click += (_, _) => Apply(true);
            panel.Children.Add(all);
        }
        panel.Children.Add(feedback);
        ShowValue(dim.TextHeightOverride);
        return panel;
    }
}
