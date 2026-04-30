using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinSender.UI.Controls;

public class IpInputBar : UserControl
{
    private readonly IconBox _icon;
    private readonly TextBox _textBox;
    private readonly RoundedButton _connectBtn;

    public event EventHandler<string>? ConnectRequested;

    public IpInputBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
               | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 52;
        Margin = new Padding(0, 0, 0, 16);

        _icon = new IconBox
        {
            IconName = "wifi",
            IconSize = 24,
            Tint = Theme.Primary,
            Location = new Point(14, 14)
        };

        _textBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = new Font(Theme.FontFamily, 12, FontStyle.Regular),
            ForeColor = Theme.TextPrimary,
            BackColor = Theme.Surface,
            Text = "",
            Location = new Point(56, 0)
        };
        _textBox.Height = TextRenderer.MeasureText("0", _textBox.Font).Height + 4;
        _textBox.Top = (Height - _textBox.Height) / 2;
        SendMessage(_textBox.Handle, EM_SETCUEBANNER, 0, "输入IP:端口连接");

        _textBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (!string.IsNullOrWhiteSpace(_textBox.Text))
                    ConnectRequested?.Invoke(this, _textBox.Text.Trim());
            }
        };

        _connectBtn = new RoundedButton
        {
            Text = "连接",
            Style = ButtonStyle.Primary,
            Size = new Size(110, 40),
            BorderRadius = 10,
            BackColor = Theme.Surface,
            Location = new Point(Width - 116, 6),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _connectBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_textBox.Text))
                ConnectRequested?.Invoke(this, _textBox.Text.Trim());
        };

        Controls.Add(_icon);
        Controls.Add(_textBox);
        Controls.Add(_connectBtn);

        Resize += (_, _) =>
        {
            _textBox.Width = Width - 56 - 130;
            _connectBtn.Location = new Point(Width - _connectBtn.Width - 6, 6);
        };
    }

    public void SetState(ButtonState s)
    {
        _connectBtn.State = ButtonState.Normal;
        _connectBtn.Style = ButtonStyle.Primary;
        _textBox.Enabled = s == ButtonState.Normal;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableHighQuality(g);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, Theme.RadiusWindow);

        using (var bg = new SolidBrush(Theme.Surface))
            g.FillPath(bg, path);

        using (var pen = new Pen(Theme.Border, 1f))
            g.DrawPath(pen, path);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, string lParam);
    private const int EM_SETCUEBANNER = 0x1501;
}