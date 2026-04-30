using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinSender.UI.Controls;

public class ToastNotification : Form
{
    private readonly System.Windows.Forms.Timer _animTimer;
    private readonly System.Windows.Forms.Timer _stayTimer;
    private float _opacity;
    private int _targetY;
    private int _currentY;
    private readonly Color _accent;
    private readonly string _msg;
    private readonly string _icon;

    private ToastNotification(string message, Color accent, string icon)
    {
        _msg = message;
        _accent = accent;
        _icon = icon;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(360, 76);
        BackColor = Theme.Surface;
        Opacity = 0;

        var screen = Screen.PrimaryScreen!.WorkingArea;
        _targetY = screen.Top + 24;
        _currentY = screen.Top - 80;
        Location = new Point(screen.Right - Width - 24, _currentY);

        _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animTimer.Tick += AnimTick;

        _stayTimer = new System.Windows.Forms.Timer { Interval = 2800 };
        _stayTimer.Tick += (_, _) =>
        {
            _stayTimer.Stop();
            _targetY = screen.Top - 80;
            _animTimer.Start();
        };

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    private void AnimTick(object? sender, EventArgs e)
    {
        bool entering = _targetY > 0;
        if (entering)
        {
            _currentY += 6;
            _opacity += 0.08f;
            if (_currentY >= _targetY)
            {
                _currentY = _targetY;
                _opacity = 1f;
                _animTimer.Stop();
                _stayTimer.Start();
            }
        }
        else
        {
            _currentY -= 6;
            _opacity -= 0.08f;
            if (_opacity <= 0)
            {
                _animTimer.Stop();
                Close();
            }
        }
        Location = new Point(Location.X, _currentY);
        Opacity = Math.Max(0, Math.Min(1, _opacity));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableHighQuality(g);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, 12);

        using (var shadow = new SolidBrush(Theme.Shadow))
        {
            var sRect = rect; sRect.Offset(0, 3);
            using var sPath = Theme.RoundedRect(sRect, 12);
            g.FillPath(shadow, sPath);
        }

        using (var bg = new SolidBrush(Theme.Surface))
            g.FillPath(bg, path);
        using (var border = new Pen(Theme.Border, 1f))
            g.DrawPath(border, path);

        using (var bar = new SolidBrush(_accent))
        {
            var barRect = new Rectangle(0, 0, 4, Height);
            using var barPath = Theme.RoundedRect(barRect, 2);
            g.FillPath(bar, barPath);
        }

        using (var iconBg = new SolidBrush(Color.FromArgb(28, _accent)))
            g.FillEllipse(iconBg, 18, (Height - 36) / 2, 36, 36);
        TextRenderer.DrawText(g, _icon, Theme.BodyMedium,
            new Rectangle(18, 0, 36, Height), _accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        TextRenderer.DrawText(g, _msg, Theme.Body,
            new Rectangle(66, 0, Width - 80, Height), Theme.TextPrimary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _animTimer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    private static void ShowOnUiThread(string msg, Color accent, string icon)
    {
        // Toast is a Form; CreateHandle/Show must run on a UI thread with a message loop.
        // Calling from a worker thread (e.g. SIPSorcery state-change callback) hangs or crashes.
        var ctx = WinFormsSync.UiContext;
        if (ctx != null)
        {
            ctx.Post(_ =>
            {
                try { new ToastNotification(msg, accent, icon).Show(); }
                catch { }
            }, null);
        }
        else
        {
            try { new ToastNotification(msg, accent, icon).Show(); } catch { }
        }
    }

    public static void ShowSuccess(string msg) => ShowOnUiThread(msg, Theme.Success, "✓");
    public static void ShowError(string msg)   => ShowOnUiThread(msg, Theme.Error,   "!");
    public static void ShowInfo(string msg)    => ShowOnUiThread(msg, Theme.PrimarySky, "i");
}
