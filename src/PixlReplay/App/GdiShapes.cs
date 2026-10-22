using System.Drawing;
using System.Drawing.Drawing2D;

namespace PixlReplay.App;

/// <summary>Shapes the tray icon, the tray menu and the save toast draw with GDI+.</summary>
static class GdiShapes
{
    /// <summary>A rectangle with corners rounded to <paramref name="radius"/>.</summary>
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
