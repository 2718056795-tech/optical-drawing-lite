using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using lcdb.DrawingFrame;
using OtoCAD.Avalonia.Slim;

namespace OtoCAD.Avalonia;

public partial class MainWindow
{
    private Control? BuildTableStyleEditor(CadCanvas canvas, DrawingFrame frame)
    {
        if (TableFormat.Read(frame) is not { } initial) return null;
        var editor = new TableStyleEditor(this, canvas, frame, initial);
        return editor.Panel;
    }

    private sealed class TableStyleEditor
    {
        private readonly MainWindow window;
        private readonly CadCanvas canvas;
        private readonly DrawingFrame frame;
        private readonly TextBox pitch, body, header;
        private readonly Button apply;
        private readonly TextBlock status = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
        private TableFormat committed;
        private TableFormatChange lastChange;
        private bool restoring;
        internal StackPanel Panel { get; } = new() { Spacing = 4, Margin = new Thickness(0, 3, 0, 9) };

        internal TableStyleEditor(MainWindow window, CadCanvas canvas, DrawingFrame frame, TableFormat initial)
        {
            this.window = window;
            this.canvas = canvas;
            this.frame = frame;
            committed = initial;
            Panel.Children.Add(new TextBlock { Text = "参数表格式（mm）", FontSize = 13, FontWeight = FontWeight.SemiBold });
            var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
            Panel.Children.Add(fields);
            pitch = Field(fields, 0, "行间距", "TableRowPitch", initial.RowPitch);
            body = Field(fields, 1, "正文字高", "TableBodyHeight", initial.BodyHeight);
            header = Field(fields, 2, "表头字高", "TableHeaderHeight", initial.HeaderHeight);
            var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1.7*") };
            Panel.Children.Add(actions);
            apply = AddButton(actions, 0, "TableFormatApply", "确定格式", () => Submit(false));
            apply.IsEnabled = false;
            if (!ReferenceEquals(canvas, window._templateCanvas) && TemplatePipeline.IsGeneratedDrawing(canvas.GetAllEntities()))
                AddButton(actions, 1, "TableFormatApplyAll", "应用格式到所有图纸", () => Submit(true));
            status.Name = "TableFormatStatus";
            Panel.Children.Add(status);
        }

        private TextBox Field(Grid fields, int column, string label, string name, double value)
        {
            var input = new TextBox { Name = name, Text = Format(value), MinWidth = 0 };
            var stack = new StackPanel { Spacing = 2, Margin = new Thickness(column == 0 ? 0 : 5, 0, 0, 0) };
            stack.Children.Add(new TextBlock { Text = label, FontSize = 12 });
            stack.Children.Add(input);
            Grid.SetColumn(stack, column);
            fields.Children.Add(stack);
            input.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && !restoring) Preview(); };
            input.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
                e.Handled = true;
                Submit(false);
            }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            return input;
        }

        private static Button AddButton(Grid row, int column, string name, string label, Action action)
        {
            var button = new Button { Name = name, Content = label, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center, FontSize = 12, Padding = new Thickness(4),
                Margin = new Thickness(column == 0 ? 0 : 5, 0, 0, 0) };
            Grid.SetColumn(button, column);
            button.Click += (_, _) => action();
            row.Children.Add(button);
            return button;
        }

        private static string Format(double number) => number.ToString("G", CultureInfo.CurrentCulture);
        private static double Parse(TextBox box) =>
            double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
            double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : double.NaN;

        private bool IsCurrent() => ReferenceEquals(canvas, window._activeCanvas) &&
            window._propertyPanel.GetLogicalDescendants().Contains(Panel) && canvas.GetAllEntities().Contains(frame);

        private TableFormat Input() => new(Parse(pitch), Parse(body), Parse(header));
        private void Preview()
        {
            if (!IsCurrent()) return;
            window._propertyPanel.ActivatePropertyDraft(Cancel);
            var next = Input();
            string? error = next.Error();
            apply.IsEnabled = error is null && next != committed;
            status.Text = error ?? (next == committed ? "" : "预览中");
            if (error is null && next != committed)
                canvas.PreviewPropertyEdit(frame, entity => next.Apply((DrawingFrame)entity));
            else canvas.PreviewPropertyEdit(frame, null);
        }

        private void Cancel()
        {
            restoring = true;
            pitch.Text = Format(committed.RowPitch);
            body.Text = Format(committed.BodyHeight);
            header.Text = Format(committed.HeaderHeight);
            restoring = false;
            apply.IsEnabled = false;
            status.Text = "";
            canvas.PreviewPropertyEdit(frame, null);
        }

        private void Submit(bool all)
        {
            if (!IsCurrent()) return;
            var next = Input();
            if (next.Error() is { } error) { status.Text = error; return; }
            var change = TableFormatChange.Between(committed, next);
            if (!all && change == default) return;
            window._propertyPanel.ActivatePropertyDraft(Cancel);
            if (all)
            {
                var batch = change != default ? change : lastChange != default ? lastChange :
                    new TableFormatChange(next.RowPitch, next.BodyHeight, next.HeaderHeight);
                var result = window.ApplyTableFormatToAll(canvas, frame, batch);
                status.Text = result.Message;
                if (!result.SourceAccepted) return;
            }
            else
            {
                canvas.ApplyEdit(() => next.Apply(frame));
                status.Text = "已应用";
            }
            if (change != default) lastChange = change;
            committed = next;
            apply.IsEnabled = false;
            window._propertyPanel.ReleasePropertyDraft(Cancel);
            canvas.PreviewPropertyEdit(frame, null);
        }
    }
}
