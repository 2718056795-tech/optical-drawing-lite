using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using lcdb;
using lcdb.Optic;
using OtoCAD.Avalonia.PropertyPanel;

namespace OtoCAD.Avalonia.Slim;

/// <summary>Runs on the initialized UI thread. Measures real arranged controls and exercises routed UI events.</summary>
public static class SidebarLayoutValidation
{
    public static IReadOnlyList<string> Run(string output)
    {
        Directory.CreateDirectory(output);
        var log = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Sidebar layout: " + message);
            log.Add("PASS " + message);
        }
        var window = new MainWindow { WindowState = WindowState.Normal };
        window.Show(); Dispatcher.UIThread.RunJobs();
        window.WindowState = WindowState.Normal;
        var size = new Size(1280, 820);
        void Layout()
        {
            Dispatcher.UIThread.RunJobs();
            window.Measure(size); window.Arrange(new Rect(size));
            Dispatcher.UIThread.RunJobs();
        }
        void Resize(int width, int height)
        {
            window.WindowState = WindowState.Normal;
            window.Width = width; window.Height = height; size = new Size(width, height);
            Layout();
        }
        T Named<T>(string name) where T : Control => window.FindControl<T>(name)
            ?? throw new InvalidOperationException("缺少实际界面控件：" + name);
        Rect InWindow(Control control)
        {
            var origin = control.TranslatePoint(new global::Avalonia.Point(0, 0), window);
            if (origin is null) throw new InvalidOperationException("控件未加入窗口：" + control.Name);
            return new Rect(origin.Value, control.Bounds.Size);
        }
        bool Visible(Control control) => control.IsVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0 &&
            control.GetVisualAncestors().OfType<Control>().All(parent => parent.IsVisible);
        static bool Contains(Rect outer, Rect inner, double tolerance = 1.5) =>
            inner.Left >= outer.Left - tolerance && inner.Top >= outer.Top - tolerance &&
            inner.Right <= outer.Right + tolerance && inner.Bottom <= outer.Bottom + tolerance;
        void Screenshot(string label)
        {
            Layout();
            var actual = ((Control)window.Content!).Bounds.Size;
            var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(actual.Width)), Math.Max(1, (int)Math.Ceiling(actual.Height)));
            string actualSuffix = Math.Abs(actual.Width - size.Width) > .5 || Math.Abs(actual.Height - size.Height) > .5
                ? $"_实际{pixels.Width}x{pixels.Height}" : "";
            string file = label + actualSuffix + ".png";
            using var bitmap = new RenderTargetBitmap(pixels, new Vector(96, 96));
            bitmap.Render(window); bitmap.Save(Path.Combine(output, file));
            log.Add(FormattableString.Invariant($"SCREENSHOT requested={size} actualClient={actual} image={pixels} file={file}"));
        }
        void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(); }
        ContentControl CoreButton(string label) => Named<Control>("MainRibbon").GetVisualDescendants()
            .OfType<ContentControl>().FirstOrDefault(button => (button is Button or SplitButton) &&
                (Equals(button.Content, label) || button.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == label)))
            ?? throw new InvalidOperationException("未找到核心功能按钮：" + label);
        void ShowPage(string page) { window.SelectSidebarPage(page); Layout(); }
        CadCanvas NewDocument(string name)
        {
            // Creating test data is setup only; switching and closing are exercised through the actual sidebar UI.
            var method = typeof(MainWindow).GetMethod("CreateDocumentTab", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("文档创建入口不存在");
            return (CadCanvas)method.Invoke(window, new object[] { name, true })!;
        }
        object DocumentItem(TabItem tab) => Named<ListBox>("SidebarDocuments").Items.OfType<Control>().Single(item => ReferenceEquals(item.Tag, tab));
        void SelectDocument(TabItem tab)
        {
            ShowPage("图纸");
            Named<ListBox>("SidebarDocuments").SelectedItem = DocumentItem(tab);
            Layout();
        }
        try
        {
            Resize(1280, 820);
            var source = new OpticalLens { R1 = 50, R2 = -60, Thickness = 4, Diameter = 10, MechanicalDiameter = 12 };
            source.FrontSurface.SemiAperture = source.BackSurface.SemiAperture = 5;
            source.ApplyMaterial("N-BK7");
            var sheet = TemplatePipeline.Build(TemplatePipeline.Preset("ISO"), source, "侧栏布局核验", "LAYOUT-17", "布局验证.zmx");
            var drawing = NewDocument("布局验证图纸"); drawing.LoadGeneratedSheet(sheet); drawing.ResetView();
            drawing.SaveCurrentScene(Path.Combine(output, "布局验证图纸.otocad"));
            Layout();
            var drawingTab = window.SidebarDocumentTabs.Single(tab => ReferenceEquals(tab.Content, drawing));
            var sidebar = Named<Control>("LeftSidebar");
            var work = Named<Control>("CanvasWorkspace");
            var nav = Named<Control>("SidebarNavigation");
            var status = Named<Border>("WorkspaceStatusBar");
            var root = (Control)window.Content!;
            foreach (var viewport in new[] { (1280, 820), (980, 680), (1920, 1080), (900, 560), (800, 520) })
            {
                Resize(viewport.Item1, viewport.Item2); ShowPage("出图");
                Screenshot($"V1.7_侧栏布局_{viewport.Item1}x{viewport.Item2}");
                var rootRect = InWindow(root); var sideRect = InWindow(sidebar); var workRect = InWindow(work);
                var canvasRect = InWindow(drawing); var statusRect = InWindow(status);
                string id = viewport.Item1 + "×" + viewport.Item2;
                var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
                var client = window.ClientSize;
                var frame = window.FrameSize is Size total ? total : client;
                double decorationWidth = Math.Max(0, frame.Width - client.Width);
                double decorationHeight = Math.Max(0, frame.Height - client.Height);
                double availableWidth = screen is null ? double.PositiveInfinity : Math.Max(0, screen.WorkingArea.Width / screen.Scaling - decorationWidth);
                double availableHeight = screen is null ? double.PositiveInfinity : Math.Max(0, screen.WorkingArea.Height / screen.Scaling - decorationHeight);
                log.Add(FormattableString.Invariant($"MEASURE requested={id} actualClient={client} frame={frame} screenWorkPixels={screen?.WorkingArea} screenScale={screen?.Scaling} availableClient={availableWidth:0.###}x{availableHeight:0.###} root={rootRect} sidebar={sideRect} canvas={canvasRect} status={statusRect}"));
                bool RequestedOrScreenLimited(double requested, double actual, double available)
                {
                    if (Math.Abs(requested - actual) <= 3) return true;
                    // Only an over-screen request may be reduced by the native window manager.
                    // Small-window layout errors must still fail; large-window content must fill its real client area.
                    return double.IsFinite(available) && requested > available + 3 &&
                           actual >= available - 3 && actual < requested;
                }
                Check(Math.Abs(rootRect.Width - client.Width) <= 3 && Math.Abs(rootRect.Height - client.Height) <= 3 &&
                      RequestedOrScreenLimited(viewport.Item1, rootRect.Width, availableWidth) &&
                      RequestedOrScreenLimited(viewport.Item2, rootRect.Height, availableHeight),
                    id + " 根内容填满实际客户区；仅超屏幕请求允许系统限高或限宽");
                Check(Visible(drawing) && Math.Abs(canvasRect.Top - rootRect.Top) <= 3, id + " 画布从窗口内容顶端开始");
                Check(Math.Abs(canvasRect.Bottom - statusRect.Top) <= 3 && Math.Abs(workRect.Bottom - statusRect.Top) <= 3, id + " 画布底部接状态栏，无额外功能行占高");
                Check(sideRect.Left <= rootRect.Left + 1.5 && sideRect.Right <= canvasRect.Left + 1.5 && sideRect.Width >= 240 &&
                      Math.Abs(sideRect.Top - rootRect.Top) <= 3, id + " 功能侧栏位于画布左侧且不覆盖图纸");
                Check(canvasRect.Width >= rootRect.Width - sideRect.Width - 12 && canvasRect.Height >= rootRect.Height - statusRect.Height - 5,
                    id + " 画布使用侧栏之外的完整可用宽高");
                Check(Contains(sideRect, InWindow(nav)) && Visible(nav), id + " 页面导航位于左侧且完整可见");
                Check(!Named<TabControl>("DocTabs").GetVisualDescendants().OfType<Control>().Any(c =>
                    c.GetType().Name.Contains("TabStrip", StringComparison.Ordinal) && Visible(c)), id + " 不再显示画布上方横向文档标签");
                Check(!window.GetVisualDescendants().OfType<PropertyPanelControl>().Any(panel => Visible(panel) && InWindow(panel).Left > canvasRect.Left),
                    id + " 画布右侧没有遗留属性栏占宽");
            }

            Resize(1280, 820); ShowPage("出图");
            foreach (string label in new[] { "导入设计", "导出图纸" })
            {
                var button = CoreButton(label); button.BringIntoView(); Layout();
                var buttonRect = InWindow(button); var sideRect = InWindow(sidebar);
                Check(Visible(button) && button.IsEnabled && Contains(sideRect, buttonRect), "核心功能“" + label + "”在左侧可滚动到并可用");
            }
            var templateActions = new[] { "模板库", "新建模板", "保存模板", "设为默认" };
            Check(!Named<Control>("MainRibbon").GetVisualDescendants().OfType<TextBlock>().Any(t => templateActions.Contains(t.Text)),
                "出图页只保留设计和导出操作，没有模板管理入口");
            ShowPage("模板");
            Screenshot("V1.10.2_模板页_1280x820");
            foreach (string label in templateActions.Concat(new[] { "零件图区", "自由表格", "编辑表格", "设计参数文字" }))
            {
                var button = CoreButton(label); button.BringIntoView(); Layout();
                Check(Visible(button) && Contains(InWindow(sidebar), InWindow(button)), "模板功能“" + label + "”保留可见入口");
            }

            var zoomButton = Named<Button>("FvbZoomIn");
            float scaleBefore = drawing.CurrentScale;
            Click(zoomButton);
            Check(Visible(zoomButton) && Contains(InWindow(sidebar), InWindow(zoomButton)) && drawing.CurrentScale > scaleBefore,
                "左侧放大按钮实际改变当前图纸视图");
            Click(Named<Button>("FvbZoomExtents"));
            Check(drawing.CurrentScale < scaleBefore * 1.01, "左侧适应整页按钮恢复完整图纸视图");

            var other = NewDocument("切换与关闭验证");
            var otherSheet = TemplatePipeline.Build(TemplatePipeline.Preset("ISO"), source, "第二张验证图纸", "LAYOUT-17B", "布局验证.zmx");
            other.LoadGeneratedSheet(otherSheet); other.ResetView(); other.SaveCurrentScene(Path.Combine(output, "切换与关闭验证.otocad"));
            Layout();
            var otherTab = window.SidebarDocumentTabs.Single(tab => ReferenceEquals(tab.Content, other));
            SelectDocument(drawingTab);
            Check(ReferenceEquals(window.ActiveCanvas, drawing) && Visible(drawing), "左侧图纸列表实际切回第一张图纸");
            SelectDocument(otherTab);
            Check(ReferenceEquals(window.ActiveCanvas, other) && Visible(other), "左侧图纸列表实际切换到第二张图纸");
            var row = (Control)DocumentItem(otherTab); row.BringIntoView(); Layout();
            var close = row.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
                button.Content is string text && (text is "✕" or "×" or "关闭"))
                ?? throw new InvalidOperationException("图纸列表没有实际关闭按钮");
            int countBeforeClose = window.SidebarDocumentTabs.Count;
            Click(close);
            Check(window.SidebarDocumentTabs.Count == countBeforeClose - 1 && !window.SidebarDocumentTabs.Contains(otherTab) &&
                  !ReferenceEquals(window.ActiveCanvas, other), "左侧关闭按钮移除已保存图纸并切换活动画布");
            SelectDocument(drawingTab);
            Screenshot("V1.7_左侧图纸列表_1280x820");

            ShowPage("出图");
            var owner = sheet.OfType<OpticalLens>().Single();
            var dimension = sheet.OfType<LinearDimension>().Single(d => d.InferDrivingParameterKey(sheet) == "Thickness");
            drawing.Select(dimension); Layout();
            var panel = Named<PropertyPanelControl>("MainPropertyPanel");
            Check(window.ActiveSidebarPage == "属性" && Visible(panel) && Contains(InWindow(sidebar), InWindow(panel)),
                "选中尺寸自动激活左侧属性页");
            T Property<T>(string name) where T : Control => panel.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
            Check(Property<TextBox>("DirectValueInput").Text == "4", "侧栏尺寸输入仍显示真实4mm");
            var valueInput = Property<TextBox>("DirectValueInput");
            valueInput.Text = "4.25";
            valueInput.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Layout();
            Check(Math.Abs(owner.Thickness / owner.DrawingBinding!.ViewScale - 4.25) < 1e-9 && drawing.IsDirty,
                "在左侧尺寸输入框按 Enter 修改真值并标记待保存");
            Property<TextBox>("DirectValueInput").Text = "-1"; Click(Property<Button>("DirectValueApply"));
            Check(Math.Abs(owner.Thickness / owner.DrawingBinding!.ViewScale - 4.25) < 1e-9 &&
                  !string.IsNullOrWhiteSpace(Property<TextBlock>("DirectValueFeedback").Text), "左侧无效尺寸仍显示原因并保留原几何");
            Property<TextBox>("DirectValueInput").Text = "4.25"; Click(Property<Button>("DirectValueApply"));
            foreach (var viewport in new[] { (1280, 820), (980, 680), (1920, 1080), (900, 560), (800, 520) })
            {
                Resize(viewport.Item1, viewport.Item2);
                var apply = Property<Button>("DirectValueApply"); apply.BringIntoView(); Layout();
                Check(Visible(apply) && Contains(InWindow(sidebar), InWindow(apply)), viewport.Item1 + "×" + viewport.Item2 + " 左侧尺寸输入应用按钮可到达");
                Screenshot($"V1.7_左侧尺寸输入_{viewport.Item1}x{viewport.Item2}");
            }
            drawing.SaveCurrentScene(Path.Combine(output, "侧栏直接尺寸编辑核验.otocad"));
            log.AddRange(ViewportFitValidation.Run());
            File.WriteAllLines(Path.Combine(output, "侧栏布局专项验证.txt"), log);
            return log;
        }
        catch
        {
            try { Screenshot("V1.7_侧栏验证失败现场"); File.WriteAllLines(Path.Combine(output, "侧栏布局专项验证.txt"), log); } catch { }
            throw;
        }
        finally { window.Close(); }
    }
}



