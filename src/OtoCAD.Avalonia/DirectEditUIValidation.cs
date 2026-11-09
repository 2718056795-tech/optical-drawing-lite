using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using lcdb;
using lcdb.DrawingFrame;
using lcdb.Optic;
using OtoCAD.Avalonia.PropertyPanel;

namespace OtoCAD.Avalonia.Slim;

/// <summary>Runs in an initialized Avalonia UI thread; uses real controls and routed button clicks.</summary>
public static class DirectEditUIValidation
{
    public static IReadOnlyList<string> Run()
    {
        var part = new OpticalLens { Diameter = 10, Thickness = 4, R1 = 50, R2 = -50 };
        part.FrontSurface.SemiAperture = part.BackSurface.SemiAperture = 5;
        var template = new Entity[]
        {
            new TemplateViewRegion { ViewScale = 3, PaperWidth = 180, PaperHeight = 110, UseFullPaperArea = false },
            new TemplateTable { Columns = new() { new() { Title = "尺寸", Rows = new() {
                "曲率半径 【前表面半径】 ±0.1", "中心厚度 【中心厚度】 ±0.02" } } } }
        };
        var entities = TemplatePipeline.Build(template, part, "UI验证", "UI-16", "控件测试");
        var owner = entities.OfType<OpticalLens>().Single();
        var table = entities.OfType<TemplateTable>().Single();
        var annotation = entities.OfType<DimensionBase>().First(e =>
            TemplatePipeline.GetDirectParameterForAnnotation(entities, e)?.Key == "Thickness");
        var panel = new PropertyPanelControl();
        int applied = 0;
        panel.SetDirectEditHandlers(e => TemplatePipeline.GetDirectParameterForAnnotation(entities, e), _ => null,
            (key, value) =>
            {
                bool ok = TemplatePipeline.TrySetDirectParameter(entities, key, value, out string error);
                if (ok) applied++;
                return (ok, error);
            });
        var log = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("直接尺寸编辑 UI: " + message);
            log.Add("PASS " + message);
        }
        T Named<T>(string name) where T : Control => panel.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Enter(string value)
        {
            Named<TextBox>("DirectValueInput").Text = value;
            Click(Named<Button>("DirectValueApply"));
        }

        panel.SetEntity(annotation);
        Check(Named<TextBox>("DirectValueInput").Text == "4", "3倍视图的尺寸输入显示真实4mm");
        string oldAnnotation = annotation.userText;
        Enter("4.375");
        Check(Math.Abs(owner.Thickness - 13.125) < 1e-10 && applied == 1,
            "真实控件应用4.375mm后镜片显示几何按3倍比例更新");
        Check(table.Columns[0].Rows[1].Contains("4.375", StringComparison.Ordinal) && annotation.userText != oldAnnotation,
            "尺寸按钮应用同时刷新参数表与标注数值");
        Check(Named<TextBox>("DirectValueInput").Text == "4.375", "成功应用后属性面板重新读取真实值");
        Enter("不是数字");
        Check(Math.Abs(owner.Thickness - 13.125) < 1e-10 && applied == 1 &&
            !string.IsNullOrEmpty(Named<TextBlock>("DirectValueFeedback").Text), "非数字输入保留旧几何并显示原因");
        Enter("-2");
        Check(Math.Abs(owner.Thickness - 13.125) < 1e-10 && applied == 1 &&
            !string.IsNullOrEmpty(Named<TextBlock>("DirectValueFeedback").Text), "非法负厚度保留旧几何并显示原因");
        Check(!panel.GetLogicalDescendants().OfType<TextBlock>().Any(c => c.Text == "userText"),
            "关联尺寸不暴露只覆盖显示文字的userText入口");
        var ordinary = new LinearDimension();
        panel.SetEntity(ordinary);
        Check(panel.GetLogicalDescendants().OfType<TextBlock>().Any(c => c.Text == "userText"),
            "普通独立尺寸仍保留原文字编辑入口");

        void BindRadiusCell()
        {
            var binding = table.TemplateTextBindings["c:0:r:0"];
            panel.SetFrameCell("尺寸", "曲率半径", binding.Expression,
                text => { table.Columns[0].Rows[0] = text; TemplatePipeline.SynchronizeGeneratedSheet(entities); },
                directParameters: TemplatePipeline.GetDirectParametersForCell(entities, table, 0, 0),
                refreshValues: BindRadiusCell, boundDescription: true);
        }
        BindRadiusCell();
        Enter("52");
        Check(Math.Abs(owner.R1 - 156) < 1e-10 && table.Columns[0].Rows[0].Contains("52.000", StringComparison.Ordinal),
            "参数表数值控件应用52mm后同步曲率与表格");
        Named<TextBox>("BoundDescriptionInput").Text = "前表面 【前表面半径】 ±0.25";
        Click(panel.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "应用描述 / 公差")));
        Check(table.Columns[0].Rows[0].StartsWith("前表面 ", StringComparison.Ordinal) &&
            table.Columns[0].Rows[0].EndsWith("±0.25", StringComparison.Ordinal) && Math.Abs(owner.R1 - 156) < 1e-10,
            "描述和公差可独立修改且保留真实曲率绑定");
        string previousRow = table.Columns[0].Rows[0];
        Named<TextBox>("BoundDescriptionInput").Text = "前表面 999 ±0.25";
        Click(panel.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "应用描述 / 公差")));
        Check(table.Columns[0].Rows[0] == previousRow, "描述入口不能移除参数字段并伪造数值");
        Enter("∞");
        Check(double.IsPositiveInfinity(owner.R1) && table.Columns[0].Rows[0].Contains("R∞", StringComparison.Ordinal),
            "曲率输入平面符号后同步平面几何和参数表");
        return log;
    }
}
