using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WinSender.WebRTC;

public static class FrameWatermarkRenderer
{
    private static readonly Font _font = new Font("Consolas", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
    private static readonly Brush _textBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
    private static readonly Brush _bgBrush = new SolidBrush(Color.FromArgb(140, 0, 0, 0));

    public static void Apply(byte[] bgra, int width, int height, double fps, long latencyMs, double mbps, string encoder = "")
    {
        if (bgra == null || bgra.Length < width * height * 4) return;

        var fpsStr  = fps > 0       ? $"{fps:F1} fps"    : "-- fps";
        var latStr  = latencyMs >= 0 ? $"{latencyMs} ms" : "-- ms";
        var mbpsStr = mbps > 0      ? $"{mbps:F1} Mbps"  : "-- Mbps";
        var resStr  = $"{width}x{height}";
        var encStr  = string.IsNullOrEmpty(encoder) ? "unknown" : encoder;
        var lines = new[] { fpsStr, latStr, mbpsStr, resStr, encStr };

        var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            IntPtr ptr = handle.AddrOfPinnedObject();
            int stride = width * 4;
            using var bmp = new Bitmap(width, height, stride, PixelFormat.Format32bppArgb, ptr);
            using var g = Graphics.FromImage(bmp);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            const int padding = 6;
            const int lineHeight = 18;
            int boxWidth = 0;
            foreach (var line in lines)
            {
                var size = g.MeasureString(line, _font);
                if (size.Width > boxWidth) boxWidth = (int)Math.Ceiling(size.Width);
            }
            boxWidth += padding * 2;
            int boxHeight = lineHeight * lines.Length + padding * 2;

            int boxX = width - boxWidth - 12;
            int boxY = 12;

            g.FillRectangle(_bgBrush, boxX, boxY, boxWidth, boxHeight);
            for (int i = 0; i < lines.Length; i++)
            {
                g.DrawString(lines[i], _font, _textBrush, boxX + padding, boxY + padding + i * lineHeight);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Watermark] Render failed: {ex.Message}");
        }
        finally
        {
            handle.Free();
        }
    }
}
