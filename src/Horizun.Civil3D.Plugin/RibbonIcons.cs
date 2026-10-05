// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - ribbon button icons, drawn in code (no binary assets).
//
// Brand: Horizun navy tile with the white horizon line (the Horizun mark) and a
// white glyph per command. Rendered at 32 px (large button) and 16 px (small /
// quick access) and frozen, so they are safe to share across ribbon threads.
// -----------------------------------------------------------------------------
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace Horizun.Civil3D.Plugin;

internal static class RibbonIcons
{
    private static readonly Color Navy = Color.FromRgb(0x1B, 0x24, 0x40);
    private static readonly Color Sky = Color.FromRgb(0x6F, 0xB7, 0xE8);

    public enum Glyph { Status, Probe, Fixture, Channel, FullWrite }

    public static ImageSource Make(Glyph glyph, int px)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var s = px / 32.0; // design grid is 32 x 32
            dc.PushTransform(new ScaleTransform(s, s));
            var tile = new SolidColorBrush(Navy); tile.Freeze();
            dc.DrawRoundedRectangle(tile, null, new Rect(0.5, 0.5, 31, 31), 6, 6);

            var white = new Pen(Brushes.White, px >= 32 ? 2.2 : 2.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            white.Freeze();
            var horizon = new Pen(new SolidColorBrush(Sky), 2.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            horizon.Freeze();
            dc.DrawLine(horizon, new Point(6, 26), new Point(26, 26)); // the Horizun horizon line

            switch (glyph)
            {
                case Glyph.Status: // pulse: the bridge is alive
                    Poly(dc, white, (5, 16), (11, 16), (14, 9), (18, 22), (21, 13), (23, 16), (27, 16));
                    break;
                case Glyph.Probe: // magnifier over code brackets
                    dc.DrawEllipse(null, white, new Point(13.5, 13), 6.5, 6.5);
                    dc.DrawLine(white, new Point(18.3, 17.8), new Point(24.5, 23.5));
                    if (px >= 32) { Poly(dc, white, (12, 10.5), (10, 13), (12, 15.5)); Poly(dc, white, (15, 10.5), (17, 13), (15, 15.5)); }
                    break;
                case Glyph.Channel: // C# channel: braces around a key slot
                    Poly(dc, white, (11, 6), (8, 7), (8, 12), (5.5, 14), (8, 16), (8, 21), (11, 22));
                    Poly(dc, white, (21, 6), (24, 7), (24, 12), (26.5, 14), (24, 16), (24, 21), (21, 22));
                    if (px >= 32) { dc.DrawEllipse(Brushes.White, null, new Point(16, 12), 2.2, 2.2); dc.DrawLine(white, new Point(16, 14), new Point(16, 18)); }
                    else dc.DrawEllipse(Brushes.White, null, new Point(16, 14), 2.5, 2.5);
                    break;
                case Glyph.FullWrite: // open padlock: the profile is raised
                    dc.DrawRoundedRectangle(null, white, new Rect(8.5, 13, 15, 10), 1.5, 1.5);
                    Poly(dc, white, (11.5, 13), (11.5, 9), (14, 6), (18, 6), (20.5, 9), (20.5, 10));
                    if (px >= 32) dc.DrawLine(white, new Point(16, 16.5), new Point(16, 19.5));
                    break;
                case Glyph.Fixture: // terrain profile over a TIN
                    Poly(dc, white, (5, 21), (11, 12), (16, 17), (21, 8), (27, 21));
                    if (px >= 32)
                    {
                        var thin = new Pen(Brushes.White, 1.0); thin.Freeze();
                        dc.DrawLine(thin, new Point(11, 12), new Point(16, 21));
                        dc.DrawLine(thin, new Point(16, 17), new Point(21, 21));
                        dc.DrawLine(thin, new Point(5, 21), new Point(27, 21));
                        dc.DrawLine(thin, new Point(21, 8), new Point(16, 21));
                    }
                    break;
            }
            dc.Pop();
        }
        var bmp = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    private static void Poly(DrawingContext dc, Pen pen, params (double X, double Y)[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(pts[0].X, pts[0].Y), false, false);
            for (var i = 1; i < pts.Length; i++) c.LineTo(new Point(pts[i].X, pts[i].Y), true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}
