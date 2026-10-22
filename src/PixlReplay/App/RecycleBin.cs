using System.Runtime.InteropServices;

namespace PixlReplay.App;

/// <summary>
/// Sends files to the Recycle Bin through the shell with explicit control over its dialogs.
/// (Microsoft.VisualBasic's DeleteFile passes FOF_NOCONFIRMATION without FOF_WANTNUKEWARNING, so a file
/// too big for the Recycle Bin was destroyed without a word even when the user chose "Move to Recycle Bin".)
/// </summary>
public static class RecycleBin
{
    [Flags]
    public enum Op : ushort
    {
        Silent = 0x0004,           // FOF_SILENT: no progress window
        NoConfirmation = 0x0010,   // FOF_NOCONFIRMATION: "Yes to all"
        AllowUndo = 0x0040,        // FOF_ALLOWUNDO: recycle instead of delete
        NoErrorUi = 0x0400,        // FOF_NOERRORUI
        WantNukeWarning = 0x4000,  // FOF_WANTNUKEWARNING: ask before destroying a file that cannot be recycled
    }

    /// <summary>The user chose "Move to Recycle Bin": no extra questions, but Windows asks before destroying a file it cannot recycle.</summary>
    public static Op UserFlags => Op.AllowUndo | Op.NoConfirmation | Op.Silent | Op.WantNukeWarning;

    /// <summary>The folder size limit runs while the user may be playing: never any window.</summary>
    public static Op BackgroundFlags => Op.AllowUndo | Op.NoConfirmation | Op.Silent | Op.NoErrorUi;

    /// <exception cref="OperationCanceledException">The user declined the permanent-delete warning.</exception>
    /// <exception cref="IOException">The shell could not delete the file.</exception>
    public static void Send(string path, Op flags)
    {
        var op = new ShFileOpStruct { wFunc = FoDelete, pFrom = Path.GetFullPath(path) + "\0", fFlags = (ushort)flags };
        int result = SHFileOperation(ref op);
        if (op.fAnyOperationsAborted)
            throw new OperationCanceledException("the user kept the file");
        if (result != 0)
            throw new IOException($"SHFileOperation failed (0x{result:X}) for {path}");
    }

    const uint FoDelete = 3;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;           // must end in two nulls: the "\0" here plus the one the marshaller adds
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref ShFileOpStruct op);
}
