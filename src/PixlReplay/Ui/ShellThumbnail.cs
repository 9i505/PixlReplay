using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace PixlReplay.Ui;

/// <summary>Video thumbnails from the Windows shell, produced on one background STA thread.</summary>
public static class ShellThumbnail
{
    static readonly BlockingCollection<(string Path, Action<BitmapSource?> Done)> Queue = [];

    static ShellThumbnail()
    {
        var worker = new Thread(() =>
        {
            foreach (var (path, done) in Queue.GetConsumingEnumerable())
            {
                BitmapSource? image = null;
                try { image = Load(path, 320, 180); }
                catch (Exception) { }
                done(image);
            }
        }) { IsBackground = true, Name = "PixlReplay thumbnails", Priority = ThreadPriority.BelowNormal };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    /// <summary>Queues a thumbnail; <paramref name="done"/> runs on the worker thread with a frozen image or null.</summary>
    public static void Request(string path, Action<BitmapSource?> done) => Queue.Add((path, done));

    static BitmapSource? Load(string path, int width, int height)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out IShellItemImageFactory factory);
        try
        {
            if (factory.GetImage(new SIZE { cx = width, cy = height }, 0x1 /* BIGGERSIZEOK */, out IntPtr hbitmap) != 0)
                return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(hbitmap);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
}
