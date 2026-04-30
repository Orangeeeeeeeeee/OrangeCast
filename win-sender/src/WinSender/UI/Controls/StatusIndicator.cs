using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinSender.UI.Controls;

public enum AppState { Idle, Searching, Connecting, Casting, Error, Reconnecting }

public class StatusIndicator : Control
{
    private AppState _state = AppState.Idle;
    private float _angle;
    private float _pulse;
    private readonly System.Windows.Forms.Timer _timer;

    public AppState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            _angle = 0; _pulse = 0;
            if (IsAnimated(value)) _timer.Start();
            else _timer.Stop();
            Invalidate();
        }
    }

    public StatusIndicator()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
               | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(96, 96);

        _timer = new System.Windows.Forms.Timer { Interval = 16 };
        _timer.Tick += (_, _) =>
        {
            _angle = (_angle + 4f) % 360f;
            _pulse += 0.03f;
            if (_pulse > 1f) _pulse = 0f;
            Invalidate();
        };
    }

    private static bool IsAnimated(AppState s) =>
        s == AppState.Searching || s == AppState.Connecting || s == AppState.Reconnecting;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableHighQuality(g);

        float cx = Width / 2f, cy = Height / 2f;
        float ringR = Math.Min(Width, Height) / 2f - 6f;
        var ringRect = new RectangleF(cx - ringR, cy - ringR, ringR * 2, ringR * 2);

        switch (_state)
        {
            case AppState.Idle:
                using (var pen = new Pen(Theme.BorderStrong, 2f))
                    g.DrawEllipse(pen, ringRect);
                using (var dot = new SolidBrush(Theme.TextMuted))
                    g.FillEllipse(dot, cx - 5, cy - 5, 10, 10);
                break;

            case AppState.Searching:
                using (var bg = new Pen(Theme.PaleSky, 3f))
                    g.DrawEllipse(bg, ringRect);
                using (var arc = new Pen(Theme.PrimarySky, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(arc, ringRect, _angle, 90);
                break;

            case AppState.Connecting:
            {
                float pulseR = ringR * (0.55f + _pulse * 0.45f);
                int alpha = (int)(180 * (1f - _pulse));
                using (var pulseBrush = new SolidBrush(Color.FromArgb(Math.Max(0, alpha), Theme.LightSky)))
                    g.FillEllipse(pulseBrush, cx - pulseR, cy - pulseR, pulseR * 2, pulseR * 2);
                using (var core = new SolidBrush(Theme.PrimarySky))
                    g.FillEllipse(core, cx - 14, cy - 14, 28, 28);
                break;
            }

            case AppState.Casting:
                using (var bg = new Pen(Theme.Border, 2f))
                    g.DrawEllipse(bg, ringRect);
                using (var coreB = new SolidBrush(Theme.Success))
                    g.FillEllipse(coreB, cx - 16, cy - 16, 32, 32);
                using (var checkPen = new Pen(Theme.White, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(checkPen, cx - 8, cy + 1, cx - 2, cy + 7);
                    g.DrawLine(checkPen, cx - 2, cy + 7, cx + 9, cy - 5);
                }
                break;

            case AppState.Reconnecting:
                using (var bg = new Pen(Theme.PaleSky, 3f))
                    g.DrawEllipse(bg, ringRect);
                using (var arc = new Pen(Theme.Amber, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(arc, ringRect, _angle, 110);
                break;

            case AppState.Error:
                using (var bg = new Pen(Theme.Border, 2f))
                    g.DrawEllipse(bg, ringRect);
                using (var coreB = new SolidBrush(Theme.Error))
                    g.FillEllipse(coreB, cx - 16, cy - 16, 32, 32);
                using (var bangPen = new Pen(Theme.White, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(bangPen, cx, cy - 8, cx, cy + 3);
                    g.FillEllipse(Brushes.White, cx - 1.5f, cy + 7, 3, 3);
                }
                break;
        }
    }
}
