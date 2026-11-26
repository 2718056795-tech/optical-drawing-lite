using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using OtoCAD.Avalonia.Services;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace OtoCAD.Avalonia.Dialogs;

/// <summary>
/// 关于对话框 — Help.About 命令的入口. 显示版本/技术栈/协议/仓库链接.
/// 点链接调系统浏览器打开 GitHub 页面.
/// </summary>
public partial class AboutDialog : Window
{
    private const string RepoUrl = "https://github.com/2718056795-tech/optical-drawing-lite";
    private const string DocsUrl = RepoUrl + "/blob/main/README.md";
    private string? _updateUrl;

    public AboutDialog()
    {
        InitializeComponent();

        // 使用程序自身程序集版本，验证宿主不会替换这里的产品版本。
        var ver = typeof(AboutDialog).Assembly.GetName().Version?.ToString(3) ?? "1.13.0";
        this.FindControl<TextBlock>("VersionText")!.Text = $"v{ver}";

        var repoText = this.FindControl<TextBlock>("RepoText")!;
        var docsText = this.FindControl<TextBlock>("DocsText")!;
        repoText.PointerPressed += (_, _) => OpenUrl(RepoUrl);
        docsText.PointerPressed += (_, _) => OpenUrl(DocsUrl);

        this.FindControl<Button>("OkBtn")!.Click += (_, _) => Close();

        var checkBtn = this.FindControl<Button>("CheckUpdateBtn")!;
        var statusText = this.FindControl<TextBlock>("UpdateStatusText")!;
        statusText.PointerPressed += (_, _) => { if (_updateUrl is not null) OpenUrl(_updateUrl); };
        checkBtn.Click += async (_, _) => await RunUpdateCheck(checkBtn, statusText);
    }

    private async Task RunUpdateCheck(Button btn, TextBlock status)
    {
        btn.IsEnabled = false;
        _updateUrl = null;
        status.Cursor = new Cursor(StandardCursorType.Arrow);
        status.Foreground = global::Avalonia.Media.Brushes.DimGray;
        status.Text = "查询中…";
        try
        {
            var info = await UpdateChecker.CheckAsync();
            if (info.HasUpdate)
            {
                status.Text = $"发现新版本 {info.ReleaseTag} — 点击此处前往下载";
                status.Foreground = global::Avalonia.Media.Brushes.DarkGreen;
                status.Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand);
                _updateUrl = info.ReleaseUrl;
            }
            else
            {
                status.Text = $"已是最新版本 (v{info.Current.ToString(3)})";
            }
        }
        catch (Exception ex)
        {
            status.Text = "检查失败: " + ex.Message;
            status.Foreground = global::Avalonia.Media.Brushes.DarkRed;
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch { /* 无浏览器 / 权限不足 — 静默 */ }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
