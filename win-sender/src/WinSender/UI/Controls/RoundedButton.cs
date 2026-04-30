using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinSender.UI.Controls;

public enum ButtonStyle { Primary, Outline, Text, Danger, NeutralOutline }
public enum ButtonState { Normal, Connecting, Connected }

public class RoundedButton : Control
{
    public ButtonStyle Style { get; set; } = ButtonStyle.Primary;
    public int BorderRadius { get; set; } = 10;
    
    private ButtonState _state = ButtonState.Normal;
    public ButtonState State 
    { 
        get => _state; 
        set 
        { 
            if (_state != value) 
            {
                _state = value;
                if (_state == ButtonState.Connecting)
                {
                    Cursor = Cursors.WaitCursor;
                    Enabled = false;
                }
                else
                {
                    Cursor = Cursors.Hand;
                    Enabled = true;
                }
                Invalidate(); 
            }
        } 
    }

    private bool _hover;
    private bool _down;

    public RoundedButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Surface;
        Cursor = Cursors.Hand;
        Font = Theme.BodyMedium;
        ForeColor = Theme.TextPrimary;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true;  Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true;  Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e)   { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var b = new SolidBrush(BackColor);
        pevent.Graphics.FillRectangle(b, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableHighQuality(g);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, BorderRadius);

        var (fill, border, text) = Resolve();
        string drawText = Text;
        if (State == ButtonState.Connecting) drawText = "连接中...";
        else if (State == ButtonState.Connected && Style == ButtonStyle.Danger) drawText = "断开连接";

        if (fill.A > 0)
        {
            using var b = new SolidBrush(fill);
            g.FillPath(b, path);
        }
        if (border.A > 0)
        {
            using var p = new Pen(border, 1.5f);
            g.DrawPath(p, path);
        }

        TextRenderer.DrawText(g, drawText, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private (Color fill, Color border, Color text) Resolve()
    {
        if (State == ButtonState.Connecting)
            return (Color.Transparent, Theme.BorderNeutral, Theme.TextMuted);

        if (State == ButtonState.Connected && Style == ButtonStyle.Danger)
            return (Theme.Destructive, Color.Transparent, Theme.White);

        if (!Enabled)
            return (Theme.PaleSky, Color.Transparent, Theme.TextMuted);

        return Style switch
        {
            ButtonStyle.Primary => (
                _down ? Theme.AmberDark : (_hover ? Theme.AmberDark : Theme.Amber),
                Color.Transparent,
                Theme.White),

            ButtonStyle.Outline => (
                _hover ? Theme.PaleSky : Color.Transparent,
                Theme.PrimarySky,
                Theme.PrimarySky),

            ButtonStyle.Text => (
                _hover ? Theme.PaleSky : Color.Transparent,
                Color.Transparent,
                _hover ? Theme.PrimarySky : Theme.TextSecondary),

            ButtonStyle.Danger => (
                _hover ? Theme.Error : Color.Transparent,
                Theme.Error,
                _hover ? Theme.White : Theme.Error),

            ButtonStyle.NeutralOutline => (
                _hover ? Theme.SurfaceMuted : Theme.Surface,
                Theme.BorderNeutral,
                Theme.TextSecondary),

            _ => (Theme.Amber, Color.Transparent, Theme.White)
        };
    }

    public static GraphicsPath GetRoundedRect(Rectangle bounds, int radius) => Theme.RoundedRect(bounds, radius);
}
