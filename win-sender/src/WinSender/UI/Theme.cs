using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace WinSender.UI;

/// <summary>
/// 橙子投屏 (OrangeCast) 设计令牌 - 扁平化 Orange 主题
/// 所有色值与 docs/design-system/MASTER.md 单一真相同步
/// </summary>
public static class Theme
{
    public static readonly Color Primary          = ColorTranslator.FromHtml("#EA580C"); // orange-600
    public static readonly Color PrimaryHover     = ColorTranslator.FromHtml("#C2410C"); // orange-700
    public static readonly Color PrimaryPressed   = ColorTranslator.FromHtml("#9A3412"); // orange-800
    public static readonly Color PrimarySoft      = ColorTranslator.FromHtml("#FFEDD5"); // orange-100
    public static readonly Color PrimaryMist      = ColorTranslator.FromHtml("#FFF7ED"); // orange-50
    public static readonly Color Accent           = ColorTranslator.FromHtml("#FED7AA"); // orange-200

    public static readonly Color White            = Color.White;
    public static readonly Color Background       = ColorTranslator.FromHtml("#FFFBF5");
    public static readonly Color Surface          = Color.White;
    public static readonly Color SurfaceMuted     = ColorTranslator.FromHtml("#FFF7ED");
    public static readonly Color SurfaceChip      = ColorTranslator.FromHtml("#FFEDD5");

    public static readonly Color TextPrimary      = ColorTranslator.FromHtml("#1C1917"); // stone-900
    public static readonly Color TextSecondary    = ColorTranslator.FromHtml("#57534E"); // stone-600
    public static readonly Color TextMuted        = ColorTranslator.FromHtml("#A8A29E"); // stone-400
    public static readonly Color TextOnPrimary    = Color.White;

    public static readonly Color Border           = ColorTranslator.FromHtml("#FED7AA");
    public static readonly Color BorderStrong     = ColorTranslator.FromHtml("#FDBA74"); // orange-300
    public static readonly Color BorderNeutral    = ColorTranslator.FromHtml("#E7E5E4"); // stone-200

    public static readonly Color Success          = ColorTranslator.FromHtml("#16A34A");
    public static readonly Color Error            = ColorTranslator.FromHtml("#DC2626");
    public static readonly Color Destructive      = ColorTranslator.FromHtml("#DC2626");
    public static readonly Color DestructiveHover = ColorTranslator.FromHtml("#B91C1C");
    public static readonly Color Warning          = ColorTranslator.FromHtml("#F59E0B");

    public static readonly Color Shadow           = Color.Transparent;
    public static readonly Color ShadowSoft       = Color.Transparent;

    public static readonly Color PrimarySky       = Primary;
    public static readonly Color LightSky         = PrimaryHover;
    public static readonly Color PaleSky          = PrimarySoft;
    public static readonly Color MistSky          = PrimaryMist;
    public static readonly Color Amber            = Primary;
    public static readonly Color AmberDark        = PrimaryHover;

    public const int RadiusWindow  = 5; // 用户硬性指定: 窗口外边框 5px
    public const int RadiusCard    = 12;
    public const int RadiusButton  = 10;
    public const int RadiusInput   = 8;
    public const int RadiusChip    = 999;

    public static readonly string FontFamily   = ResolveFontFamily(
        "Inter", "HarmonyOS Sans SC", "Microsoft YaHei UI", "Segoe UI Variable", "Segoe UI");

    public static readonly string MonoFamily   = ResolveFontFamily(
        "JetBrains Mono", "Cascadia Mono", "Cascadia Code", "Consolas");

    public static readonly Font Hero        = new(FontFamily, 32, FontStyle.Bold);
    public static readonly Font H1          = new(FontFamily, 22, FontStyle.Bold);
    public static readonly Font H2          = new(FontFamily, 16, FontStyle.Bold);
    public static readonly Font H3          = new(FontFamily, 13, FontStyle.Bold);
    public static readonly Font Body        = new(FontFamily, 11, FontStyle.Regular);
    public static readonly Font BodyMedium  = new(FontFamily, 11, FontStyle.Bold);
    public static readonly Font Small       = new(FontFamily, 9,  FontStyle.Regular);
    public static readonly Font Caption     = new(FontFamily, 8,  FontStyle.Regular);
    public static readonly Font Mono        = new(MonoFamily,  12, FontStyle.Regular);
    public static readonly Font MonoLarge   = new(MonoFamily,  16, FontStyle.Bold);
    public static readonly Font BigDigit    = new(FontFamily,  44, FontStyle.Bold);
    public static readonly Font InputLarge  = new(FontFamily,  14, FontStyle.Regular);

    public static Font TitleFont      => H1;
    public static Font CardTitleFont  => H2;
    public static Font BodyFont       => Body;
    public static Font SubTextFont    => Small;
    public static Font BigDigitFont   => BigDigit;

    public static void EnableHighQuality(Graphics g)
    {
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        if (radius <= 0) { p.AddRectangle(r); return p; }
        int d = radius * 2;
        p.AddArc(r.X,         r.Y,          d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y,          d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d,   0, 90);
        p.AddArc(r.X,         r.Bottom - d, d, d,  90, 90);
        p.CloseFigure();
        return p;
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        if (radius <= 0) { p.AddRectangle(r); return p; }
        float d = radius * 2;
        p.AddArc(r.X,         r.Y,          d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y,          d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d,   0, 90);
        p.AddArc(r.X,         r.Bottom - d, d, d,  90, 90);
        p.CloseFigure();
        return p;
    }

    private static string ResolveFontFamily(params string[] candidates)
    {
        using var col = new InstalledFontCollection();
        var installed = col.Families;
        foreach (var name in candidates)
        {
            foreach (var f in installed)
                if (string.Equals(f.Name, name, System.StringComparison.OrdinalIgnoreCase))
                    return name;
        }
        return SystemFonts.MessageBoxFont?.Name ?? "Segoe UI";
    }
}
