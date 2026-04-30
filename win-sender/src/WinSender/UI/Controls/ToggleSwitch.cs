using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinSender.UI.Controls;

public class ToggleSwitch : Control
{
    private bool _checked;
    private float _thumbX;
    private readonly System.Windows.Forms.Timer _animTimer;

    private const int TrackW  = 52;
    private const int TrackH  = 28;
    private const int ThumbD  = 22;
    private const int ThumbMargin = 3;
    private const int ThumbOffX = ThumbMargin;
    private const int ThumbOnX  = TrackW - ThumbD - ThumbMargin;
    private const int AnimStep  = 4;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            _animTimer.Start();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
               | ControlStyles.SupportsTransparentBackColor, true);
        Size      = new Size(TrackW, TrackH);
        BackColor = Color.Transparent;
        Cursor    = Cursors.Hand;
        _thumbX   = ThumbOffX;

        _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animTimer.Tick += AnimTimer_Tick;
    }

    private void AnimTimer_Tick(object? sender, EventArgs e)
    {
        float target = _checked ? ThumbOnX : ThumbOffX;
        float delta  = target - _thumbX;
        if (Math.Abs(delta) <= AnimStep)
        {
            _thumbX = target;
            _animTimer.Stop();
        }
        else
        {
            _thumbX += delta > 0 ? AnimStep : -AnimStep;
        }
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !_checked;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.PixelOffsetMode   = PixelOffsetMode.HighQuality;

        float t      = (_thumbX - ThumbOffX) / (float)(ThumbOnX - ThumbOffX);
        var trackColor = Lerp(Color.FromArgb(0xD1, 0xD5, 0xDB), Theme.Primary, t);

        var trackRect = new RectangleF(0, 0, TrackW, TrackH);
        using var trackBrush = new SolidBrush(trackColor);
        using var trackPath  = RoundedRect(trackRect, TrackH / 2f);
        g.FillPath(trackBrush, trackPath);

        float thumbY = (TrackH - ThumbD) / 2f;
        var thumbRect = new RectangleF(_thumbX, thumbY, ThumbD, ThumbD);
        using var shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0));
        g.FillEllipse(shadowBrush, thumbRect.X + 1, thumbRect.Y + 1.5f, ThumbD, ThumbD);
        using var thumbBrush = new SolidBrush(Color.White);
        g.FillEllipse(thumbBrush, thumbRect);
    }

    private static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X,         r.Y,          d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y,          d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d,   0, 90);
        p.AddArc(r.X,         r.Bottom - d, d, d,  90, 90);
        p.CloseFigure();
        return p;
    }
}
