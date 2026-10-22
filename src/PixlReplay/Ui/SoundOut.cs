using System.Runtime.InteropServices;

namespace PixlReplay.Ui;

/// <summary>
/// Plays 16-bit stereo sound on the default output with the plain Windows waveOut API, and says how much has been
/// heard (the preview player's clock). Opens paused; <see cref="Start"/> begins once enough is queued.
/// </summary>
sealed class SoundOut : IDisposable
{
    const uint WaveMapper = unchecked((uint)-1);
    const uint CallbackNull = 0;
    const int HeaderDone = 1;
    const uint TimeSamples = 2, TimeBytes = 4;

    readonly IntPtr _device;
    readonly int _rate, _bytesPerFrame;
    readonly bool _mute;
    readonly List<(IntPtr Header, IntPtr Data)> _queued = [];
    readonly object _lock = new();
    long _writtenBytes;
    bool _disposed;

    /// <param name="mute">Plays silence instead (self-tests on a PC someone is using). The app's volume is left alone:
    /// Windows remembers it per app.</param>
    public SoundOut(int sampleRate, int channels, bool mute = false)
    {
        _rate = sampleRate;
        _bytesPerFrame = channels * 2;
        _mute = mute;
        var format = new WaveFormatEx
        {
            FormatTag = 1, // PCM
            Channels = (ushort)channels,
            SamplesPerSec = (uint)sampleRate,
            AvgBytesPerSec = (uint)(sampleRate * _bytesPerFrame),
            BlockAlign = (ushort)_bytesPerFrame,
            BitsPerSample = 16,
        };
        uint result = waveOutOpen(out _device, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, CallbackNull);
        if (result != 0)
            throw new InvalidOperationException($"No sound output (waveOut error {result}).");
        waveOutPause(_device);
    }

    /// <summary>Scales 16-bit samples in place by <paramref name="gain"/> (0 = silence, 1 = as they are, 2 = twice as loud,
    /// held at the loudest a sample can be instead of wrapping round).</summary>
    public static void Scale(Span<byte> pcm, double gain)
    {
        if (gain == 1)
            return;
        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(pcm);
        if (gain <= 0)
        {
            samples.Clear();
            return;
        }
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (short)Math.Clamp(samples[i] * gain, short.MinValue, short.MaxValue);
    }

    /// <summary>Queues a piece of sound (copied, so the buffer can be reused at once), at <paramref name="gain"/>.</summary>
    public void Write(ReadOnlySpan<byte> pcm, double gain = 1)
    {
        if (pcm.IsEmpty)
            return;
        lock (_lock)
        {
            if (_disposed)
                return;
            Recycle();
            IntPtr data = Marshal.AllocHGlobal(pcm.Length);
            unsafe
            {
                var target = new Span<byte>((void*)data, pcm.Length);
                if (_mute)
                    target.Clear();
                else
                {
                    pcm.CopyTo(target);
                    Scale(target, gain);
                }
            }
            var header = new WaveHeader { Data = data, BufferLength = (uint)pcm.Length };
            IntPtr headerPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
            Marshal.StructureToPtr(header, headerPtr, false);
            uint size = (uint)Marshal.SizeOf<WaveHeader>();
            if (waveOutPrepareHeader(_device, headerPtr, size) != 0 || waveOutWrite(_device, headerPtr, size) != 0)
            {
                waveOutUnprepareHeader(_device, headerPtr, size);
                Marshal.FreeHGlobal(headerPtr);
                Marshal.FreeHGlobal(data);
                return;
            }
            _queued.Add((headerPtr, data));
            _writtenBytes += pcm.Length;
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (!_disposed)
                waveOutRestart(_device);
        }
    }

    /// <summary>Seconds of sound heard so far.</summary>
    public double PlayedSeconds
    {
        get
        {
            lock (_lock)
            {
                if (_disposed)
                    return 0;
                var time = new MmTime { Type = TimeSamples };
                if (waveOutGetPosition(_device, ref time, (uint)Marshal.SizeOf<MmTime>()) != 0)
                    return 0;
                return time.Type == TimeBytes ? time.Value / (double)(_rate * _bytesPerFrame) : time.Value / (double)_rate;
            }
        }
    }

    /// <summary>Seconds of sound written so far.</summary>
    public double WrittenSeconds
    {
        get
        {
            lock (_lock)
                return _writtenBytes / (double)(_rate * _bytesPerFrame);
        }
    }

    /// <summary>Frees the pieces the sound card has finished with.</summary>
    void Recycle()
    {
        int flagsOffset = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags));
        uint size = (uint)Marshal.SizeOf<WaveHeader>();
        for (int i = _queued.Count - 1; i >= 0; i--)
        {
            var (header, data) = _queued[i];
            if ((Marshal.ReadInt32(header, flagsOffset) & HeaderDone) == 0)
                continue;
            waveOutUnprepareHeader(_device, header, size);
            Marshal.FreeHGlobal(header);
            Marshal.FreeHGlobal(data);
            _queued.RemoveAt(i);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            // Reset hands every queued piece back as done, so all of them can be freed.
            waveOutReset(_device);
            uint size = (uint)Marshal.SizeOf<WaveHeader>();
            foreach (var (header, data) in _queued)
            {
                waveOutUnprepareHeader(_device, header, size);
                Marshal.FreeHGlobal(header);
                Marshal.FreeHGlobal(data);
            }
            _queued.Clear();
            waveOutClose(_device);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WaveFormatEx
    {
        public ushort FormatTag, Channels;
        public uint SamplesPerSec, AvgBytesPerSec;
        public ushort BlockAlign, BitsPerSample, Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength, BytesRecorded;
        public IntPtr User;
        public uint Flags, Loops;
        public IntPtr Next, Reserved;
    }

    /// <summary>MMTIME: a type and an 8-byte union (only the 32-bit counts are used).</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct MmTime
    {
        public uint Type;
        public uint Value;
        public uint Rest;
    }

    [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr device, uint deviceId, ref WaveFormatEx format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutPause(IntPtr device);
    [DllImport("winmm.dll")] static extern uint waveOutRestart(IntPtr device);
    [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr device);
    [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr device);
    [DllImport("winmm.dll")] static extern uint waveOutGetPosition(IntPtr device, ref MmTime time, uint size);
}
