using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinSender.UI;

public enum TrayState { Idle, Connecting, Casting, Error }

/// <summary>
/// 系统托盘 GUI 入口。保留对 Program.cs --tray 模式的契约：
/// ConnectRequested / DisconnectRequested 事件、SetState、ShowBalloon、TrayState 枚举。
/// 新版 UI 走 MainWindow 单列居中流；TrayApp 提供"最小化到托盘 + 快捷断开 + 退出"。
/// </summary>
public sealed class TrayApp : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _miStatus;
    private readonly ToolStripMenuItem _miOpen;
    private readonly ToolStripMenuItem _miDisconnect;
    private readonly ToolStripMenuItem _miExit;

    private TrayState _state = TrayState.Idle;
    private string _detail = "";

    /// <summary>(target, code) — 由 Program.cs --tray 路径触发；新版 UI 走 MainWindow，本事件保留兼容。</summary>
    public event Action<string, string>? ConnectRequested;
    public event Action? DisconnectRequested;

    /// <summary>由 Program.cs 调用，用于在打开主窗口时同步 UI。</summary>
    public event Action? OpenMainRequested;

    public TrayApp()
    {
        _menu = new ContextMenuStrip
        {
            Renderer = new TrayMenuRenderer(),
            ShowImageMargin = false,
            Font = Theme.Body
        };

        _miStatus = new ToolStripMenuItem("状态: 未连接") { Enabled = false };
        _miOpen = new ToolStripMenuItem("打开主窗口");
        _miDisconnect = new ToolStripMenuItem("断开连接") { Visible = false };
        _miExit = new ToolStripMenuItem("退出");

        _miOpen.Click += (_, _) => OpenMainRequested?.Invoke();
        _miDisconnect.Click += (_, _) => DisconnectRequested?.Invoke();
        _miExit.Click += (_, _) => Application.Exit();

        _menu.Items.Add(_miStatus);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_miOpen);
        _menu.Items.Add(_miDisconnect);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_miExit);

        _icon = new NotifyIcon
        {
            Icon = BuildIcon(Theme.TextSecondary),
            Text = "橙子投屏",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _icon.DoubleClick += (_, _) => OpenMainRequested?.Invoke();
    }

    public void SetState(TrayState state, string detail = "")
    {
        _state = state;
        _detail = detail;

        Color c = state switch
        {
            TrayState.Idle       => Theme.TextSecondary,
            TrayState.Connecting => Theme.PrimarySky,
            TrayState.Casting    => Theme.Success,
            TrayState.Error      => Theme.Error,
            _                    => Theme.TextSecondary
        };
        var oldIcon = _icon.Icon;
        _icon.Icon = BuildIcon(c);
        oldIcon?.Dispose();

        _miStatus.Text = state switch
        {
            TrayState.Idle       => "状态: 未连接",
            TrayState.Connecting => "状态: 连接中…",
            TrayState.Casting    => $"状态: 投屏中 → {detail}",
            TrayState.Error      => $"状态: 错误 - {detail}",
            _                    => "状态: 未知"
        };
        _icon.Text = _miStatus.Text.Length > 63 ? _miStatus.Text.Substring(0, 60) + "..." : _miStatus.Text;
        _miDisconnect.Visible = state == TrayState.Casting || state == TrayState.Connecting;
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(3000);
    }

    /// <summary>Program.cs --tray 路径会调用 ConnectRequested。提供主动触发入口（保留契约）。</summary>
    public void RequestConnect(string target, string code) => ConnectRequested?.Invoke(target, code);

    private static Icon BuildIcon(Color dotColor)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var ring = new Pen(Theme.PrimarySky, 2.2f);
            g.DrawEllipse(ring, 4, 4, 24, 24);
            using var dot = new SolidBrush(dotColor);
            g.FillEllipse(dot, 11, 11, 10, 10);
        }
        IntPtr h = bmp.GetHicon();
        var ico = (Icon)Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        return ico;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}

internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    public TrayMenuRenderer() : base(new TrayColors()) { base.RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.TextPrimary : Theme.TextMuted;
        base.OnRenderItemText(e);
    }
}

internal sealed class TrayColors : ProfessionalColorTable
{
    public override Color MenuItemSelected           => Theme.MistSky;
    public override Color MenuItemSelectedGradientBegin => Theme.MistSky;
    public override Color MenuItemSelectedGradientEnd   => Theme.MistSky;
    public override Color MenuItemBorder             => Theme.PrimarySky;
    public override Color ToolStripDropDownBackground => Theme.Surface;
    public override Color ImageMarginGradientBegin   => Theme.Surface;
    public override Color ImageMarginGradientMiddle  => Theme.Surface;
    public override Color ImageMarginGradientEnd     => Theme.Surface;
    public override Color SeparatorDark              => Theme.Border;
    public override Color SeparatorLight             => Theme.Border;
}
