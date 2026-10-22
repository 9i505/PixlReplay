using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32.SafeHandles;

namespace PixlReplay.Ui;

/// <summary>
/// PixlReplay's mouse pointers: Windows' own (white, in the user's scheme and size), changing shape as usual for text, moving
/// and resizing, except that there is no hand (the arrow shows instead). The editor's tools have pointers of their own at
/// the same size, white with a dark edge like Windows' arrow: a pen to draw, and a cross with a shape, a drop or crop
/// corners. <see cref="Install"/> puts them on every window.
/// </summary>
public static class ThemeCursors
{
    public enum Shape { Arrow, IBeam, Cross, Move, SizeWE, SizeNS, SizeNWSE, SizeNESW, Pen, ShapeTool, BlurTool, CropTool }

    /// <summary>The editor's tool pointers, drawn by PixlReplay (the rest are Windows' own).</summary>
    public static bool IsTool(Shape shape) => shape >= Shape.Pen;

    /// <summary>A pointer's picture: BGRA with straight alpha, rows top down, and its hot spot.</summary>
    public sealed record CursorPicture(int Width, int Height, (int X, int Y) Hot, byte[] Bgra);

    static readonly Dictionary<Shape, Cursor> Made = [];
    static readonly object Gate = new();

    /// <summary>The pointer for <paramref name="shape"/>: Windows' own, or a tool's (Windows' cross if it could not be made).</summary>
    public static Cursor Get(Shape shape)
    {
        if (!IsTool(shape))
            return Fallback(shape);
        lock (Gate)
        {
            if (!Made.TryGetValue(shape, out var cursor))
                Made[shape] = cursor = Make(shape) ?? Fallback(shape);
            return cursor;
        }
    }

    static Cursor Fallback(Shape shape) => shape switch
    {
        Shape.IBeam => Cursors.IBeam,
        Shape.Cross or Shape.Pen or Shape.ShapeTool or Shape.BlurTool or Shape.CropTool => Cursors.Cross,
        Shape.Move => Cursors.SizeAll,
        Shape.SizeWE => Cursors.SizeWE,
        Shape.SizeNS => Cursors.SizeNS,
        Shape.SizeNWSE => Cursors.SizeNWSE,
        Shape.SizeNESW => Cursors.SizeNESW,
        _ => Cursors.Arrow,
    };

    /// <summary>An element's pointer as PixlReplay shows it: Windows' own, but the hand is the arrow.</summary>
    public static Cursor Themed(Cursor cursor) => cursor == Cursors.Hand ? Cursors.Arrow : cursor;

    static bool _installed;

    /// <summary>PixlReplay's pointers on every window of this process (once).</summary>
    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), Mouse.QueryCursorEvent, new QueryCursorEventHandler(OnQueryCursor), handledEventsToo: true);
    }

    /// <summary>
    /// Runs on each element under the pointer, inside out (after the element's own choice): a chosen hand becomes the
    /// arrow, and at the top of a window or popup, where nothing chose one, the arrow (buttons too: there is no hand).
    /// </summary>
    static void OnQueryCursor(object sender, QueryCursorEventArgs e)
    {
        if (e.Handled && e.Cursor != null)
            e.Cursor = Themed(e.Cursor);
        if (!e.Handled && sender is Visual top && VisualTreeHelper.GetParent(top) == null)
        {
            e.Cursor = Cursors.Arrow;
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- Windows' pointer

    static readonly Color Edge = Color.FromRgb(0x14, 0x10, 0x10);

    static int Id(Shape shape) => shape switch
    {
        Shape.IBeam => 32513,
        Shape.Cross => 32515,
        Shape.SizeNWSE => 32642,
        Shape.SizeNESW => 32643,
        Shape.SizeWE => 32644,
        Shape.SizeNS => 32645,
        Shape.Move => 32646,
        _ => 32512, // the arrow (and the size the tools are drawn at)
    };

    /// <summary>Windows' pointer as it is now (the user's scheme and size), or null if it could not be read.</summary>
    public static CursorPicture? SystemPicture(Shape shape)
    {
        IntPtr cursor = LoadCursor(IntPtr.Zero, Id(shape));
        if (cursor == IntPtr.Zero || !GetIconInfo(cursor, out var info))
            return null;
        try
        {
            if (!Bitmap(info.hbmMask, out int width, out int maskHeight, out byte[] mask))
                return null;
            if (info.hbmColor != IntPtr.Zero && Bitmap(info.hbmColor, out _, out int height, out byte[] colour))
            {
                // A colour pointer: its own alpha, or (none at all) the mask's see-through parts.
                bool hasAlpha = false;
                for (int i = 3; i < colour.Length && !hasAlpha; i += 4)
                    hasAlpha = colour[i] != 0;
                if (!hasAlpha)
                    for (int i = 0; i < width * height; i++)
                        colour[i * 4 + 3] = mask[i * 4] > 127 ? (byte)0 : (byte)255;
                return new CursorPicture(width, height, (info.xHotspot, info.yHotspot), colour);
            }
            // A black and white pointer: the mask holds AND above XOR. See-through, black, white, and inverted. Inverted
            // parts (the text beam, the cross) show on any background by inverting it; here they are white with a thin
            // dark edge round them instead, so they still show on light and dark alike.
            int h = maskHeight / 2;
            var pixels = new byte[width * h * 4];
            var inverted = new bool[width * h];
            for (int i = 0; i < width * h; i++)
            {
                bool and = mask[i * 4] > 127, xor = mask[(i + width * h) * 4] > 127;
                byte v = xor ? (byte)255 : (byte)0;
                pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = v;
                pixels[i * 4 + 3] = and && !xor ? (byte)0 : (byte)255;
                inverted[i] = and && xor;
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (pixels[i * 4 + 3] != 0)
                        continue;
                    bool besideInverted = false;
                    for (int dy = -1; dy <= 1 && !besideInverted; dy++)
                        for (int dx = -1; dx <= 1 && !besideInverted; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            besideInverted = nx >= 0 && ny >= 0 && nx < width && ny < h && inverted[ny * width + nx];
                        }
                    if (besideInverted)
                        pixels[i * 4 + 3] = 255; // black: the edge
                }
            return new CursorPicture(width, h, (info.xHotspot, info.yHotspot), pixels);
        }
        finally
        {
            if (info.hbmMask != IntPtr.Zero)
                DeleteObject(info.hbmMask);
            if (info.hbmColor != IntPtr.Zero)
                DeleteObject(info.hbmColor);
        }
    }

    /// <summary>A tool's pointer picture, drawn at the size of Windows' arrow (null for Windows' own pointers).</summary>
    public static CursorPicture? Picture(Shape shape) =>
        IsTool(shape) && SystemPicture(Shape.Arrow) is { } arrow ? Tool(shape, arrow.Width) : null;

    // ---------------------------------------------------------------- the editor's tools

    /// <summary>A tool pointer drawn in a 32-unit box scaled to <paramref name="size"/>: white with a dark edge, like the arrow.</summary>
    static CursorPicture Tool(Shape shape, int size)
    {
        double scale = size / 32.0;
        var white = Brushes.White;
        var edge = new Pen(new SolidColorBrush(Edge), 1.4) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var visual = new DrawingVisual();
        Point hot;
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            if (shape == Shape.Pen)
            {
                // A pen from its tip (the hot spot, bottom left) up and right: body, a dark tip, a band near the end.
                hot = new Point(3, 29);
                Vector along = new Vector(1, -1) / Math.Sqrt(2), side = new Vector(1, 1) / Math.Sqrt(2);
                Point At(double a, double s) => hot + along * a + side * s;
                var body = new StreamGeometry();
                using (var g = body.Open())
                {
                    g.BeginFigure(At(0, 0), true, true);
                    g.PolyLineTo([At(7, 3.2), At(27, 3.2), At(29.5, 1.6), At(29.5, -1.6), At(27, -3.2), At(7, -3.2)], true, true);
                }
                dc.DrawGeometry(white, edge, body);
                var tip = new StreamGeometry();
                using (var g = tip.Open())
                {
                    g.BeginFigure(At(0, 0), true, true);
                    g.PolyLineTo([At(3.4, 1.55), At(3.4, -1.55)], true, true);
                }
                dc.DrawGeometry(new SolidColorBrush(Edge), null, tip);
                dc.DrawLine(edge, At(7, 3.2), At(7, -3.2));
                dc.DrawLine(edge, At(23, 3.2), At(23, -3.2));
            }
            else
            {
                // A cross (the hot spot) with a small sign of the tool below and to the right.
                hot = new Point(10, 10);
                var cross = new GeometryGroup();
                cross.Children.Add(new RectangleGeometry(new Rect(9, 1.5, 2, 6)));
                cross.Children.Add(new RectangleGeometry(new Rect(9, 12.5, 2, 6)));
                cross.Children.Add(new RectangleGeometry(new Rect(1.5, 9, 6, 2)));
                cross.Children.Add(new RectangleGeometry(new Rect(12.5, 9, 6, 2)));
                dc.DrawGeometry(white, edge, cross);
                switch (shape)
                {
                    case Shape.ShapeTool:
                        dc.DrawRoundedRectangle(white, edge, new Rect(16, 19, 8, 8), 1.2, 1.2);
                        dc.DrawEllipse(white, edge, new Point(26, 21), 4, 4);
                        break;
                    case Shape.BlurTool:
                        dc.DrawGeometry(white, edge, Geometry.Parse("M23,16 C26,20 28.5,22.5 28.5,25 A5.5,5.5 0 0 1 17.5,25 C17.5,22.5 20,20 23,16 Z"));
                        dc.DrawEllipse(new SolidColorBrush(Edge), null, new Point(21.2, 25.2), 1.1, 1.1);
                        break;
                    default: // crop
                        var corners = Geometry.Parse("M17,15 L17,26 L28,26 M21,22 L21,18 M14,18 L25,18 L25,29");
                        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Edge), 3.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, corners);
                        dc.DrawGeometry(null, new Pen(white, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, corners);
                        break;
                }
            }
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int a = pixels[i + 3];
            if (a is > 0 and < 255)
                for (int c = 0; c < 3; c++)
                    pixels[i + c] = (byte)Math.Min(255, pixels[i + c] * 255 / a); // straight alpha, as cursors keep it
        }
        return new CursorPicture(size, size, ((int)Math.Round(hot.X * scale), (int)Math.Round(hot.Y * scale)), pixels);
    }

    // ---------------------------------------------------------------- a cursor from a picture

    static Cursor? Make(Shape shape)
    {
        // Called inside the lock (from Get): the native pointer lives as long as the cursor does.
        if (Picture(shape) is not { } picture)
            return null;
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr colour = IntPtr.Zero, mask = IntPtr.Zero;
        try
        {
            var header = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = picture.Width,
                biHeight = -picture.Height, // rows top down
                biPlanes = 1,
                biBitCount = 32,
            };
            colour = CreateDIBSection(screen, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (colour == IntPtr.Zero)
                return null;
            Marshal.Copy(picture.Bgra, 0, bits, picture.Bgra.Length);
            mask = CreateBitmap(picture.Width, picture.Height, 1, 1, new byte[(picture.Width + 15) / 16 * 2 * picture.Height]);
            var info = new ICONINFO { fIcon = false, xHotspot = picture.Hot.X, yHotspot = picture.Hot.Y, hbmMask = mask, hbmColor = colour };
            IntPtr handle = CreateIconIndirect(ref info);
            if (handle == IntPtr.Zero)
                return null;
            return CursorInteropHelper.Create(new CursorHandle(handle));
        }
        finally
        {
            if (colour != IntPtr.Zero)
                DeleteObject(colour);
            if (mask != IntPtr.Zero)
                DeleteObject(mask);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>A bitmap's pixels as 32-bit BGRA, rows top down.</summary>
    static bool Bitmap(IntPtr bitmap, out int width, out int height, out byte[] pixels)
    {
        width = height = 0;
        pixels = [];
        if (bitmap == IntPtr.Zero || GetObject(bitmap, Marshal.SizeOf<BITMAP>(), out BITMAP bm) == 0)
            return false;
        width = bm.bmWidth;
        height = bm.bmHeight;
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };
        pixels = new byte[width * height * 4];
        IntPtr screen = GetDC(IntPtr.Zero);
        try
        {
            return GetDIBits(screen, bitmap, 0, (uint)height, pixels, ref header, 0) == height;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>A cursor made here: destroyed with DestroyIcon when WPF lets it go.</summary>
    sealed class CursorHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public CursorHandle(IntPtr handle) : base(true) => SetHandle(handle);

        protected override bool ReleaseHandle() => DestroyIcon(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr instance, int id);
    [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("user32.dll")] static extern IntPtr CreateIconIndirect(ref ICONINFO info);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr gdiObject);
    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr gdiObject, int size, out BITMAP bitmap);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);
}
