using System;
using System.Drawing;
using System.Windows.Forms;
using WinSender.Discovery;

namespace WinSender.UI.Controls;

public class DeviceCard : UserControl
{
    private readonly TvDevice _device;
    private readonly RoundedButton _connectBtn;
    private readonly IconBox _monitorIcon;
    private bool _hovered;

    public TvDevice Device => _device;

    public event EventHandler<TvDevice>? ConnectClicked;

    public DeviceCard(TvDevice device)
    {
        _device = device;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
               | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(560, 96);
        Margin = new Padding(0, 0, 0, 14);

        _connectBtn = new RoundedButton
        {
            Text = "连接",
            Style = ButtonStyle.Primary,
            Size = new Size(96, 40),
            BorderRadius = 10,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _connectBtn.Click += (_, _) => ConnectClicked?.Invoke(this, _device);
        Controls.Add(_connectBtn);

        _monitorIcon = new IconBox
        {
            IconName = "monitor",
            IconSize = 24,
            Tint = Theme.Primary
        };
        Controls.Add(_monitorIcon);

        Resize += (_, _) => LayoutChildren();
        LayoutChildren();

        MouseEnter += OnEnter;
        MouseLeave += OnLeave;
        _connectBtn.MouseEnter += OnEnter;
        _connectBtn.MouseLeave += OnLeave;
        _monitorIcon.MouseEnter += OnEnter;
        _monitorIcon.MouseLeave += OnLeave;
    }

    public void SetConnectionState(ButtonState s)
    {
        _connectBtn.Style = s == ButtonState.Connected ? ButtonStyle.Danger : ButtonStyle.Primary;
        _connectBtn.State = s;
    }

    private void LayoutChildren()
    {
        _connectBtn.Location = new Point(Width - _connectBtn.Width - 24, (Height - _connectBtn.Height) / 2);
        _monitorIcon.Location = new Point(24 + 10, Height / 2 - 12);
    }

    private void OnEnter(object? s, EventArgs e) { _hovered = true; Invalidate(); }
    private void OnLeave(object? s, EventArgs e)
    {
        var p = PointToClient(Cursor.Position);
        if (ClientRectangle.Contains(p)) return;
        _hovered = false; Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableHighQuality(g);

        var rect = new Rectangle(2, 2, Width - 4, Height - 4);

        using var path = Theme.RoundedRect(rect, 14);
        using (var bg = new SolidBrush(Theme.Surface))
            g.FillPath(bg, path);
        using (var pen = new Pen(_hovered ? Theme.PrimarySky : Theme.Border, 1.5f))
            g.DrawPath(pen, path);

        int leftIconX = 24;
        int iconCY = Height / 2;
        using (var iconBg = new SolidBrush(Theme.PaleSky))
            g.FillEllipse(iconBg, leftIconX, iconCY - 22, 44, 44);

        int textX = leftIconX + 60;
        TextRenderer.DrawText(g, _device.Name, Theme.H2,
            new Point(textX, iconCY - 22), Theme.TextPrimary, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, $"{_device.Host} : {_device.Port}", Theme.Small,
            new Point(textX, iconCY + 4), Theme.TextSecondary, TextFormatFlags.NoPadding);
    }
}
