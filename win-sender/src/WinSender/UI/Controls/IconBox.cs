using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Svg;

namespace WinSender.UI.Controls;

// SVG-based icon control. Loads Lucide SVG from Assets/icons/{Name}.svg,
// recolors stroke/fill to Tint, and renders at the configured size.
// Replaces Material.Icons.WinForms (which has no real WinForms package).
public class IconBox : Control
{
    private string _iconName = "";
    private Color _tint = Color.Black;
    private int _iconSize = 24;
    private SvgDocument? _svg;

    public string IconName
    {
        get => _iconName;
        set
        {
            if (_iconName == value) return;
            _iconName = value;
            ReloadSvg();
            Invalidate();
        }
    }

    public Color Tint
    {
        get => _tint;
        set
        {
            if (_tint == value) return;
            _tint = value;
            ApplyTint();
            Invalidate();
        }
    }

    public int IconSize
    {
        get => _iconSize;
        set
        {
            _iconSize = value;
            Size = new Size(value, value);
            Invalidate();
        }
    }

    public IconBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
               | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(_iconSize, _iconSize);
    }

    private static string ResolveIconPath(string name)
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "Assets", "icons", name + ".svg");
    }

    private void ReloadSvg()
    {
        _svg = null;
        if (string.IsNullOrEmpty(_iconName)) return;
        var path = ResolveIconPath(_iconName);
        if (!File.Exists(path)) return;
        try
        {
            _svg = SvgDocument.Open(path);
            ApplyTint();
        }
        catch { _svg = null; }
    }

    private void ApplyTint()
    {
        if (_svg == null) return;
        var color = new SvgColourServer(_tint);
        // Recolor root + all descendants. Lucide uses stroke-only paths.
        _svg.Stroke = color;
        _svg.Fill = SvgPaintServer.None;
        RecolorRecursive(_svg, color);
    }

    private static void RecolorRecursive(SvgElement el, SvgColourServer color)
    {
        foreach (var child in el.Children)
        {
            if (child is SvgVisualElement vis)
            {
                vis.Stroke = color;
                if (vis.Fill is SvgColourServer cs && cs.Colour != Color.Transparent && cs != SvgPaintServer.None)
                {
                    // Lucide is stroke-only; force-clear any solid fill so the icon doesn't fill in.
                    vis.Fill = SvgPaintServer.None;
                }
            }
            RecolorRecursive(child, color);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_svg == null) return;
        var g = e.Graphics;
        Theme.EnableHighQuality(g);
        try
        {
            using var bmp = _svg.Draw(_iconSize, _iconSize);
            g.DrawImage(bmp, 0, 0, _iconSize, _iconSize);
        }
        catch { }
    }

    public new Color ForeColor
    {
        get => Tint;
        set => Tint = value;
    }
}
