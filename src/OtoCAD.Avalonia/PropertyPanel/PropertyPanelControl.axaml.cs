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
using lcdb.DrawingFrame;

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
    private Func<Action, Action>? _editRecorder;
    private Func<Entity, DirectParameter?>? _resolveDirectParameter;
    private Func<Entity, string?>? _measurementExplanation;
    private Func<string, double, (bool Success, string Error)>? _applyDirectParameter;
    private Func<string, double, BatchPropertyResult>? _applyParameterToAll;
    private int _editorGeneration;
    internal Func<Control?>? TableStyleEditor { get; set; }
    internal Action<Entity, Action<Entity>?>? PreviewEdit { get; set; }
    private Action? _cancelPropertyDraft;

    internal void ActivatePropertyDraft(Action cancel)
    {
        if (_cancelPropertyDraft == cancel) return;
        CancelPropertyDraft();
        _cancelPropertyDraft = cancel;
    }

    internal void ReleasePropertyDraft(Action cancel)
    {
        if (_cancelPropertyDraft == cancel) _cancelPropertyDraft = null;
    }

    internal bool CancelPropertyDraft()
    {
        if (_cancelPropertyDraft is not { } cancel) return false;
        _cancelPropertyDraft = null;
        cancel();
        return true;
    }

    private void ResetEditors()
    {
        CancelPropertyDraft();
        _editorGeneration++;
        _contentPanel.Children.Clear();
    }

    internal Func<DrawingFrame, PropertyCellHit, Func<string, string, BatchPropertyResult>?>? BatchForField { get; set; }

    public void SetDirectEditHandlers(Func<Entity, DirectParameter?> resolve,
        Func<Entity, string?> measurementExplanation,
        Func<string, double, (bool Success, string Error)> apply,
        Func<string, double, BatchPropertyResult>? applyToAll = null)
    {
        _resolveDirectParameter = resolve;
        _measurementExplanation = measurementExplanation;
        _applyDirectParameter = apply;
        _applyParameterToAll = applyToAll;
    }

    public PropertyPanelControl()
    {
        InitializeComponent();
        _titleText = this.FindControl<TextBlock>("TitleText")!;
        _contentPanel = this.FindControl<StackPanel>("ContentPanel")!;
        AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && CancelPropertyDraft()) e.Handled = true;
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// 设置 onChanged 回调 (修改属性后画布重绘). MainWindow 启动时注入一次.
    /// </summary>
    public void SetChangeNotifier(Action onChanged, Func<Action, Action>? editRecorder = null)
    {
        _onChanged = onChanged;
        _editRecorder = editRecorder;
    }

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
        ResetEditors();
        var notify = _onChanged ?? (() => { });
        Action recorder = _editRecorder?.Invoke(notify) ?? notify;
        void Changed() => recorder();
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
        Control EntityText(string key, string label, string value, Action<string> fallback)
        {
            var frame = entity as DrawingFrame;
            var field = new PropertyCellHit(label, key, default);
            bool named = frame is not null && TemplatePipeline.IsNamedFrameField(frame, field);
            if (named && TemplatePipeline.IsGeneratedScaleField(frame!, field))
                return MakeReadonlyRow(label, value + "（由零件图区的视图比例决定）");
            void Apply(string text)
            {
                if (!named) { fallback(text); return; }
                TemplatePipeline.SetFrameFieldText(frame!, field, text);
                frame!.InvalidateRenderCache();
                Changed();
            }
            var batch = named ? BatchForField?.Invoke(frame!, field) : null;
            void Preview(string? draft) => PreviewEdit?.Invoke(entity, draft is null ? null : copy =>
            {
                if (named) TemplatePipeline.SetFrameFieldText((DrawingFrame)copy, field, draft);
                else copy.GetType().GetProperty(key)?.SetValue(copy, draft);
            });
            BatchPropertyResult ApplyAll(string original, string edited)
            {
                var result = batch!(original, edited);
                // Batch commands record their own undo step. Subsequent entity property edits start here.
                if (result.SourceAccepted) recorder = _editRecorder?.Invoke(notify) ?? notify;
                return result;
            }
            return MakeTextEditor(label, (frame is null ? "EntityProperty" : "FrameProperty") + key, value, Apply,
                multiline: (frame is not null && TemplatePipeline.IsTechnicalNotesField(frame, field)) ||
                           (entity is MText && key == nameof(MText.Value)) || value.Contains('\n'),
                onApplyAll: batch is null ? null : ApplyAll, preview: Preview,
                allowOverrideUnchanged: named && TemplatePipeline.CanOverrideFrameField(frame!, field));
        }

        if (entity is DimensionBase dim)
            _contentPanel.Children.Add(MakeDimensionTextHeightEditor(dim, Changed));

        var controls = GenericPropertyBuilder.Build(entity, Changed,
            protectMeasuredValue: parameter is not null || !string.IsNullOrEmpty(explanation),
            textEditor: entity is DrawingFrame or Text or MText ? EntityText : null);
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
        Action? refreshValues = null, bool boundDescription = false, bool multiline = false,
        Func<string, string, BatchPropertyResult>? onApplyAll = null,
        bool allowOverrideUnchanged = false, bool readOnlyScale = false, Action<string?>? preview = null)
    {
        _titleText.Text = string.IsNullOrEmpty(label)
            ? $"属性区: {zone}"
            : $"属性区: {zone} · {label}";
        ResetEditors();

        if (!string.IsNullOrEmpty(label))
            _contentPanel.Children.Add(MakeReadonlyRow("指标", label));

        if (readOnlyScale)
        {
            _contentPanel.Children.Add(MakeReadonlyRow("实际比例", currentText));
            _contentPanel.Children.Add(new TextBlock { Text = "图纸比例由模板中零件图区的视图比例决定。", TextWrapping = TextWrapping.Wrap });
            return;
        }

        if (TableStyleEditor?.Invoke() is { } styleEditor) _contentPanel.Children.Add(styleEditor);

        if (directParameters is { Count: > 0 })
            foreach (var parameter in directParameters)
                _contentPanel.Children.Add(MakeDirectParameterEditor(parameter, refreshValues));

        if (cellKey is not null && onSetKey is not null)
            _contentPanel.Children.Add(MakeTextEditor("键 key", "FrameCellKey", cellKey, onSetKey));

        if (boundDescription)
        {
            var expected = Regex.Matches(currentText, "【[^】]+】").Select(m => m.Value).OrderBy(x => x).ToArray();
            string? Validate(string value)
            {
                var actual = Regex.Matches(value, "【[^】]+】").Select(m => m.Value).OrderBy(x => x);
                return expected.SequenceEqual(actual) ? null : "请保留原有【参数】字段；尺寸数值可在上方修改。";
            }
            var editor = MakeTextEditor("【…】由镜片参数填写，可编辑描述和公差。", "FrameCellValue",
                currentText, onSet, true, onApplyAll, validate: Validate, description: true, preview: preview);
            _contentPanel.Children.Add(new Expander
            {
                Header = "描述 / 公差文字", Content = editor,
                IsExpanded = directParameters is null || directParameters.Count == 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            });
        }
        else
        {
            _contentPanel.Children.Add(MakeTextEditor(multiline ? null : "值", "FrameCellValue",
                currentText, onSet, multiline, onApplyAll, allowOverrideUnchanged, preview: preview));
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

    public void SetTechnicalNotes(IsoLensDrawingFrame frame, Action onChanged)
    {
        var field = new PropertyCellHit("技术要求", "Notes", default);
        SetFrameCell("技术要求", "", frame.Notes, text =>
        {
            TemplatePipeline.SetFrameFieldText(frame, field, text.Replace("\r\n", "\n").Replace('\r', '\n'));
            frame.InvalidateRenderCache();
            onChanged();
        }, multiline: true, onApplyAll: BatchForField?.Invoke(frame, field),
            allowOverrideUnchanged: TemplatePipeline.CanOverrideFrameField(frame, field),
            preview: text => PreviewEdit?.Invoke(frame, text is null ? null : copy =>
                TemplatePipeline.SetFrameFieldText((DrawingFrame)copy, field, text)));
    }

    private Control MakeTextEditor(string? label, string id, string initial, Action<string> onSet,
        bool multiline = false, Func<string, string, BatchPropertyResult>? onApplyAll = null,
        bool allowOverrideUnchanged = false, Func<string, string?>? validate = null, bool description = false,
        Action<string?>? preview = null)
    {
        int generation = _editorGeneration;
        string committed = initial, previous = initial;
        bool allowInitialOverride = allowOverrideUnchanged;
        var box = new TextBox
        {
            Name = description ? "BoundDescriptionInput" : id + "Input", Text = initial, FontSize = 12,
            AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? (description ? 55 : 150) : 30, MaxHeight = multiline ? 320 : double.PositiveInfinity
        };
        var apply = new Button
        {
            Name = id + "Apply", Content = description ? "应用描述 / 公差" : "确定", Classes = { "accent" },
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center
        };
        var status = new TextBlock { Name = id + "Status", FontSize = 11, TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Spacing = 5 };
        if (label is not null) panel.Children.Add(new TextBlock { Text = label, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        panel.Children.Add(apply);
        void State(string message, bool error = false)
        {
            status.Text = message;
            status.Foreground = error ? Brushes.Firebrick : Foreground;
            apply.IsEnabled = allowInitialOverride || !string.Equals(box.Text ?? "", committed, StringComparison.Ordinal);
        }
        bool resetting = false;
        void CancelDraft()
        {
            resetting = true;
            box.Text = committed;
            resetting = false;
            preview?.Invoke(null);
            State("");
        }
        void Submit(bool all)
        {
            if (generation != _editorGeneration) return;
            if (_cancelPropertyDraft != (Action)CancelDraft) CancelPropertyDraft();
            string edited = box.Text ?? "";
            bool changed = edited != committed;
            if (!all && !changed && !allowInitialOverride) return;
            if (validate?.Invoke(edited) is string invalid) { State(invalid, true); return; }
            try
            {
                string message;
                if (all && onApplyAll is not null)
                {
                    var result = onApplyAll(changed ? committed : previous, edited);
                    if (!result.SourceAccepted) { State(result.Message, true); return; }
                    message = result.Message;
                }
                else { onSet(edited); message = "已应用"; }
                if (changed) previous = committed;
                committed = edited;
                allowInitialOverride = false;
                ReleasePropertyDraft(CancelDraft);
                preview?.Invoke(null);
                State(message);
            }
            catch (InvalidOperationException ex) { State(ex.Message, true); }
        }
        apply.Click += (_, _) => Submit(false);
        if (onApplyAll is not null)
        {
            var all = new Button { Name = id + "ApplyAll", Content = "应用到所有图纸",
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            all.Click += (_, _) => Submit(true);
            panel.Children.Add(all);
        }
        panel.Children.Add(status);
        box.GotFocus += (_, _) =>
        {
            if (generation == _editorGeneration && _cancelPropertyDraft != (Action)CancelDraft) CancelPropertyDraft();
        };
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || resetting || generation != _editorGeneration) return;
            string text = box.Text ?? "";
            if (text == committed)
            {
                if (_cancelPropertyDraft == (Action)CancelDraft)
                {
                    ReleasePropertyDraft(CancelDraft);
                    preview?.Invoke(null);
                }
                State("");
                return;
            }
            ActivatePropertyDraft(CancelDraft);
            preview?.Invoke(text);
            State(preview is null ? "未应用" : "预览中 · 未应用");
        };
        box.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (generation != _editorGeneration) return;
            if (e.Key == Key.Escape && (box.Text ?? "") != committed)
            {
                box.Text = committed;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && e.KeyModifiers == (multiline ? KeyModifiers.Control : KeyModifiers.None))
            {
                e.Handled = true;
                Submit(false);
            }
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        State("");
        return panel;
    }

    private Control MakeDirectParameterEditor(DirectParameter parameter, Action? refresh, bool isDimension = false)
    {
        int generation = _editorGeneration;
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
        input.GotFocus += (_, _) => { if (generation == _editorGeneration) CancelPropertyDraft(); };
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
            if (generation != _editorGeneration) return;
            CancelPropertyDraft();
            feedback.Foreground = Brushes.Firebrick;
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
        if (_applyParameterToAll is { } applyToAll)
        {
            var all = new Button
            {
                Name = "DirectValueApplyAll", Tag = parameter.Key, Content = "应用到所有图纸",
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center
            };
            all.Click += (_, _) =>
            {
                if (generation != _editorGeneration) return;
                CancelPropertyDraft();
                feedback.Foreground = Brushes.Firebrick;
                if (!TryParseDirectValue(input.Text, parameter.AllowInfinity, out double value, out string issue))
                { feedback.Text = issue; return; }
                try
                {
                    var result = applyToAll(parameter.Key, value);
                    feedback.Foreground = result.SourceAccepted ? Foreground : Brushes.Firebrick;
                    feedback.Text = result.Message;
                }
                catch (InvalidOperationException ex) { feedback.Text = ex.Message; }
            };
            panel.Children.Add(all);
        }
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
        Action<PropertyZoneCatalog.Indicator> onAddIndicator, Action<int> onRemoveRow, Action<string?>? preview = null)
    {
        _titleText.Text = $"属性区: {zoneTitle}";
        ResetEditors();

        _contentPanel.Children.Add(MakeTextEditor("区标题", "FrameZoneTitle", zoneTitle, onRenameZone, preview: preview));
        if (TableStyleEditor?.Invoke() is { } styleEditor) _contentPanel.Children.Add(styleEditor);

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
        ResetEditors();
    }

    private static Entity GetLineFromVm(LineViewModel vm)
    {
        // 反射拿 _line 私有字段 (兼容旧 API, 仅在 BindFirstLineToPropertyPanel 调用)
        var fld = typeof(LineViewModel).GetField("_line", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return (Entity)fld!.GetValue(vm)!;
    }
}
