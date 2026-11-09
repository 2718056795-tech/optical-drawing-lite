using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Input;
using OtoCAD.Avalonia.Slim;
using lcdb;

namespace OtoCAD.Avalonia.PropertyPanel;

/// <summary>
/// Phase 1F: 通用属性面板 dispatcher.
/// 选中实体后, 用 GenericPropertyBuilder 动态构建编辑器 UI.
/// </summary>
public partial class PropertyPanelControl : UserControl
{
    private TextBlock _titleText = null!;
    private StackPanel _contentPanel = null!;
    private Action? _onChanged;
    private Func<Entity, DirectParameter?>? _resolveDirectParameter;
    private Func<Entity, string?>? _measurementExplanation;
    private Func<string, double, (bool Success, string Error)>? _applyDirectParameter;

    public void SetDirectEditHandlers(Func<Entity, DirectParameter?> resolve,
        Func<Entity, string?> measurementExplanation,
        Func<string, double, (bool Success, string Error)> apply)
    {
        _resolveDirectParameter = resolve;
        _measurementExplanation = measurementExplanation;
        _applyDirectParameter = apply;
    }

    public PropertyPanelControl()
    {
        InitializeComponent();
        _titleText = this.FindControl<TextBlock>("TitleText")!;
        _contentPanel = this.FindControl<StackPanel>("ContentPanel")!;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// 设置 onChanged 回调 (修改属性后画布重绘). MainWindow 启动时注入一次.
    /// </summary>
    public void SetChangeNotifier(Action onChanged) => _onChanged = onChanged;

    /// <summary>
    /// 设置当前编辑目标. 传 null 清空.
    /// 兼容旧 SetTarget(LineViewModel) 调用 (POC-04 时代), 新代码请用 SetEntity.
    /// </summary>
    public void SetTarget(LineViewModel? vm)
    {
        if (vm is null) { ClearContent("属性: (无选中)"); return; }
        // 旧 VM 路径已废弃, 但保持兼容
        SetEntityInternal(GetLineFromVm(vm), 1);
    }

    /// <summary>新 API: 直接传 Entity (Phase 1F). totalCount 用于多选时友好提示.</summary>
    public void SetEntity(Entity? entity, int totalCount = 1)
    {
        if (entity is null) { ClearContent("属性: (无选中)"); return; }
        SetEntityInternal(entity, totalCount);
    }

    private void SetEntityInternal(Entity entity, int totalCount)
    {
        _titleText.Text = totalCount > 1
            ? $"属性: {entity.GetType().Name} (+ 其他 {totalCount - 1} 个)"
            : $"属性: {entity.GetType().Name}";
        _contentPanel.Children.Clear();
        var onChanged = _onChanged ?? (() => { });
        var parameter = _resolveDirectParameter?.Invoke(entity);
        var explanation = _measurementExplanation?.Invoke(entity);
        if (parameter is not null)
        {
            _contentPanel.Children.Add(MakeDirectParameterEditor(parameter,
                () => SetEntityInternal(entity, totalCount), isDimension: true));
        }
        else if (!string.IsNullOrEmpty(explanation))
        {
            _contentPanel.Children.Add(new TextBlock
            {
                Text = explanation, TextWrapping = TextWrapping.Wrap,
                FontSize = 12, Margin = new Thickness(4, 6)
            });
        }
        var controls = GenericPropertyBuilder.Build(entity, onChanged,
            protectMeasuredValue: parameter is not null || !string.IsNullOrEmpty(explanation));
        foreach (var c in controls) _contentPanel.Children.Add(c);
    }

    /// <summary>
    /// 绑定模板图框属性区某值格进行编辑(三列式)。区/指标只读显示,值可改。
    /// onSet 由调用方提供:写回该格文本(IPropertyZoneFrame.SetCellText)+ 重绘。
    /// </summary>
    public void SetFrameCell(string zone, string label, string currentText, Action<string> onSet,
        Action? onAddRow = null, Action? onDeleteRow = null,
        string? cellKey = null, Action<string>? onSetKey = null,
        IReadOnlyList<DirectParameter>? directParameters = null,
        Action? refreshValues = null, bool boundDescription = false, bool multiline = false)
    {
        _titleText.Text = string.IsNullOrEmpty(label)
            ? $"属性区: {zone}"
            : $"属性区: {zone} · {label}";
        _contentPanel.Children.Clear();

        if (!string.IsNullOrEmpty(label))
            _contentPanel.Children.Add(MakeReadonlyRow("指标", label));

        if (directParameters is { Count: > 0 })
            foreach (var parameter in directParameters)
                _contentPanel.Children.Add(MakeDirectParameterEditor(parameter, refreshValues));

        // 自定义属性"键"编辑 (供清单/AI 按键引用; 失焦才写回, 改键代价大不逐字符)。
        if (cellKey is not null && onSetKey is not null)
        {
            var keyInput = new TextBox { Text = cellKey, FontSize = 12 };
            keyInput.LostFocus += (_, _) => onSetKey(keyInput.Text ?? "");
            _contentPanel.Children.Add(GenericPropertyBuilder.MakeLabeledField("键 key", keyInput));
        }

        if (boundDescription)
        {
            var textPanel = new StackPanel { Spacing = 4 };
            textPanel.Children.Add(new TextBlock
            {
                Text = "【…】由镜片参数自动填写；可修改其余描述和公差。",
                TextWrapping = TextWrapping.Wrap, FontSize = 11
            });
            var box = new TextBox
            {
                Name = "BoundDescriptionInput", Text = currentText, AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap, MinHeight = 55, FontSize = 12
            };
            textPanel.Children.Add(box);
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, FontSize = 11 };
            var applyText = new Button { Content = "应用描述 / 公差", HorizontalAlignment = HorizontalAlignment.Stretch };
            applyText.Click += (_, _) =>
            {
                string edited = box.Text ?? "";
                var expected = Regex.Matches(currentText, "【[^】]+】").Select(m => m.Value).OrderBy(x => x).ToArray();
                var actual = Regex.Matches(edited, "【[^】]+】").Select(m => m.Value).OrderBy(x => x).ToArray();
                if (!expected.SequenceEqual(actual))
                {
                    error.Text = "请保留原有【参数】字段；尺寸数值请在上方输入后应用。";
                    return;
                }
                onSet(edited);
                error.Text = "";
                refreshValues?.Invoke();
            };
            textPanel.Children.Add(applyText);
            textPanel.Children.Add(error);
            _contentPanel.Children.Add(new Expander
            {
                Header = "描述 / 公差文字", Content = textPanel,
                IsExpanded = directParameters is null || directParameters.Count == 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            });
        }
        else
        {
            var box = new TextBox { Name = "FrameCellValueInput", Text = currentText, FontSize = 12,
                AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinHeight = multiline ? 170 : 30, MaxHeight = multiline ? 340 : double.PositiveInfinity };
            box.TextChanged += (_, _) => onSet(box.Text ?? "");
            _contentPanel.Children.Add(multiline ? box : GenericPropertyBuilder.MakeLabeledField("值", box));
        }

        if (onAddRow is not null)
        {
            var add = new Button { Content = "＋ 添加指标行", Margin = new Thickness(0, 6, 0, 0), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch };
            add.Click += (_, _) => onAddRow();
            _contentPanel.Children.Add(add);
        }
        if (onDeleteRow is not null)
        {
            var del = new Button { Content = "－ 删除此行", Margin = new Thickness(0, 6, 0, 0), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch };
            del.Click += (_, _) => onDeleteRow();
            _contentPanel.Children.Add(del);
        }
    }

    public void SetTechnicalNotes(lcdb.DrawingFrame.IsoLensDrawingFrame frame, Action onChanged)
    {
        SetFrameCell("技术要求", "", frame.Notes, text =>
        {
            frame.Notes = text.Replace("\r\n", "\n").Replace('\r', '\n');
            frame.NotesRowHeight = Math.Max(frame.NotesRowHeight, 8 + frame.Notes.Split('\n').Length * 3.2);
            frame.InvalidateRenderCache(); onChanged();
        }, multiline: true);
    }

    private Control MakeDirectParameterEditor(DirectParameter parameter, Action? refresh, bool isDimension = false)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(4, 4, 4, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = isDimension ? "尺寸数值(mm)" : $"{parameter.Label} ({parameter.Unit})",
            FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap
        });
        if (isDimension)
            panel.Children.Add(new TextBlock { Text = parameter.Label, FontSize = 11 });
        var input = new TextBox
        {
            Name = "DirectValueInput", Tag = parameter.Key,
            Text = double.IsInfinity(parameter.Value) ? "∞" : parameter.Value.ToString("G12", CultureInfo.InvariantCulture),
            Watermark = parameter.AllowInfinity ? "真实数值，基础曲率半径可填 ∞" : "输入真实数值",
            FontSize = 14, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        panel.Children.Add(input);
        var feedback = new TextBlock
        {
            Name = "DirectValueFeedback", Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap, FontSize = 11
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var apply = new Button { Classes = { "accent" }, Name = "DirectValueApply", Tag = parameter.Key, Content = "应用", MinWidth = 72 };
        void Submit()
        {
            if (!TryParseDirectValue(input.Text, parameter.AllowInfinity, out double value, out string error))
            {
                feedback.Text = error;
                return;
            }
            if (_applyDirectParameter is null)
            {
                feedback.Text = "当前图纸未连接到可编辑的镜片参数。";
                return;
            }
            var result = _applyDirectParameter(parameter.Key, value);
            if (!result.Success)
            {
                feedback.Text = string.IsNullOrEmpty(result.Error) ? "未能修改，原尺寸已保留。" : result.Error;
                return;
            }
            feedback.Text = "";
            refresh?.Invoke();
        }
        apply.Click += (_, _) => Submit();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Submit(); e.Handled = true; } };
        buttons.Children.Add(apply);
        if (parameter.AllowInfinity)
        {
            var plane = new Button { Content = "填入半径 ∞" };
            plane.Click += (_, _) => { input.Text = "∞"; input.Focus(); };
            buttons.Children.Add(plane);
        }
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock
        {
            Text = parameter.AllowInfinity ? "半径∞表示基础曲率为零，非球面系数仍保留。应用后同步镜片、标注和参数表。" : "应用后同步镜片轮廓、标注和参数表。",
            FontSize = 11, TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(feedback);
        return panel;
    }

    public static bool TryParseDirectValue(string? text, bool allowInfinity, out double value, out string error)
    {
        string input = (text ?? "").Trim();
        if (allowInfinity && (input is "∞" or "+∞" or "-∞" ||
            input.Equals("inf", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("infinity", StringComparison.OrdinalIgnoreCase)))
        {
            value = double.PositiveInfinity;
            error = "";
            return true;
        }
        if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !double.IsFinite(value))
        {
            error = allowInfinity ? "请输入有效数字；基础曲率半径可填 ∞。原尺寸未改变。" : "请输入有效数字，单位为 mm。原尺寸未改变。";
            return false;
        }
        error = "";
        return true;
    }

    /// <summary>
    /// B3/UX: 点中属性区(区/列)→ 顶部可编辑"区标题"输入框 + 该区"可加指标"复选框清单。
    /// onRenameZone(新标题):改区标题(不重绑面板,保持输入焦点)。
    /// onAddIndicator(指标) / onRemoveRow(行下标):增删后由调用方重绑本面板。
    /// </summary>
    public void SetFrameZone(string zoneTitle, IReadOnlyList<string> currentRows,
        Action<string> onRenameZone,
        Action<PropertyZoneCatalog.Indicator> onAddIndicator, Action<int> onRemoveRow)
    {
        _titleText.Text = $"属性区: {zoneTitle}";
        _contentPanel.Children.Clear();

        // 区标题(可编辑)
        var titleBox = new TextBox { Text = zoneTitle, FontSize = 12 };
        titleBox.TextChanged += (_, _) => onRenameZone(titleBox.Text ?? "");
        _contentPanel.Children.Add(GenericPropertyBuilder.MakeLabeledField("区标题", titleBox));

        _contentPanel.Children.Add(new TextBlock
        {
            Text = "勾选添加指标到该区(取消则移除):",
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 6),
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
        });

        foreach (var ind in PropertyZoneCatalog.For(zoneTitle))
        {
            int existingIdx = -1;
            for (int i = 0; i < currentRows.Count; i++)
                if (MatchesIndicator(currentRows[i], ind.Prefix)) { existingIdx = i; break; }

            var cb = new CheckBox
            {
                Content = ind.Label,
                IsChecked = existingIdx >= 0,     // 在附事件前设初值 → 不触发回调
                FontSize = 11,
                Margin = new Thickness(0, 1)
            };
            int capturedIdx = existingIdx;
            var capturedInd = ind;
            cb.IsCheckedChanged += (_, _) =>
            {
                bool nowChecked = cb.IsChecked == true;
                if (nowChecked && capturedIdx < 0) onAddIndicator(capturedInd);
                else if (!nowChecked && capturedIdx >= 0) onRemoveRow(capturedIdx);
            };
            _contentPanel.Children.Add(cb);
        }
    }

    private static bool MatchesIndicator(string row, string prefix)
        => !string.IsNullOrEmpty(prefix) &&
           (row ?? "").TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static Control MakeReadonlyRow(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*") };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
        var v = new TextBlock
        {
            Text = value,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold
        };
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        return grid;
    }

    private void ClearContent(string title)
    {
        _titleText.Text = title;
        _contentPanel.Children.Clear();
    }

    private static Entity GetLineFromVm(LineViewModel vm)
    {
        // 反射拿 _line 私有字段 (兼容旧 API, 仅在 BindFirstLineToPropertyPanel 调用)
        var fld = typeof(LineViewModel).GetField("_line", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return (Entity)fld!.GetValue(vm)!;
    }
}
