using FFmpeg.AutoGen;

namespace PixlReplay.Engine.Share;

/// <summary>Opens a clip's video decoder on the graphics card (AV1 needs it), else on the CPU.</summary>
static unsafe class HardwareDecoding
{
    static readonly object DeviceLock = new();
    static AVBufferRef* _shared;
    static Timer? _idle;

    /// <summary>How long the device stays after its last reader closes: readers come in bursts (thumbnails, scrubbing).</summary>
    internal static TimeSpan KeepIdle = TimeSpan.FromSeconds(15);

    internal static bool Holding
    {
        get
        {
            lock (DeviceLock)
                return _shared != null;
        }
    }

    /// <summary>
    /// A reference to the one D3D11 device every decoder in this process shares (FFmpeg shares a device between
    /// decoders, under its own lock). A device made for each reader cost time and left two handles behind every time.
    /// The caller gives its reference back with <see cref="Return"/>. A device the card has lost (driver reset) is
    /// replaced, so the next reader works again. Null when there is no D3D11 device.
    /// </summary>
    internal static AVBufferRef* SharedDevice()
    {
        lock (DeviceLock)
        {
            if (_shared != null && Removed(_shared))
            {
                AVBufferRef* lost = _shared;
                ffmpeg.av_buffer_unref(&lost);
                _shared = null;
            }
            if (_shared == null)
            {
                AVBufferRef* created = null;
                if (ffmpeg.av_hwdevice_ctx_create(&created, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0) < 0)
                    return null;
                _shared = created;
            }
            return ffmpeg.av_buffer_ref(_shared);
        }
    }

    /// <summary>
    /// Frees a reference from <see cref="SharedDevice"/>. Once nothing has used the device for <see cref="KeepIdle"/>
    /// it goes too, with its memory and the card's memory, so a closed editor or player leaves nothing behind.
    /// </summary>
    internal static void Return(AVBufferRef* device)
    {
        if (device == null)
            return;
        ffmpeg.av_buffer_unref(&device);
        lock (DeviceLock)
        {
            if (_shared == null)
                return;
            _idle ??= new Timer(_ => ReleaseIfUnused());
            _idle.Change(KeepIdle, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Lets the device go when only this class still holds it (a reader still open is told by its references).</summary>
    static void ReleaseIfUnused()
    {
        lock (DeviceLock)
        {
            if (_shared == null || ffmpeg.av_buffer_get_ref_count(_shared) != 1)
                return;
            AVBufferRef* last = _shared;
            ffmpeg.av_buffer_unref(&last);
            _shared = null;
        }
    }

    /// <summary>ID3D11Device::GetDeviceRemovedReason (vtable slot 39) says the device is gone.</summary>
    static bool Removed(AVBufferRef* device)
    {
        var context = (AVHWDeviceContext*)device->data;
        IntPtr d3d = *(IntPtr*)context->hwctx; // AVD3D11VADeviceContext.device comes first
        if (d3d == IntPtr.Zero)
            return true;
        var vtable = *(IntPtr**)d3d;
        return ((delegate* unmanaged[Stdcall]<IntPtr, int>)vtable[39])(d3d) != 0;
    }

    /// <summary>
    /// A decoder for the clip's video: FFmpeg's own decoders (av1, h264, hevc) on the graphics card when
    /// <paramref name="hardwareDecode"/> and the card can (<paramref name="device"/> is then the shared device, given back with
    /// <see cref="Return"/>),
    /// else on the CPU. Asked for by name: the default AV1 decoder in OBS's build is libaom on the CPU, about ten times
    /// slower at 1440p. A graphics-card decoder that cannot open throws <see cref="HardwareDecodeFailedException"/>.
    /// </summary>
    public static AVCodecContext* OpenVideo(AVStream* v, bool hardwareDecode, out AVBufferRef* device)
    {
        device = null;
        AVCodecID id = v->codecpar->codec_id;
        AVCodec* codec = null;
        if (hardwareDecode)
        {
            codec = ffmpeg.avcodec_find_decoder_by_name(ffmpeg.avcodec_get_name(id));
            AVBufferRef* created;
            if (codec != null && SupportsD3D11(codec) && (created = SharedDevice()) != null)
                device = created;
            else
                codec = null;
        }
        if (codec == null)
            codec = ffmpeg.avcodec_find_decoder(id);
        if (codec == null)
            throw new ShareException($"No decoder for {ffmpeg.avcodec_get_name(id)}.");
        AVCodecContext* decoder = ffmpeg.avcodec_alloc_context3(codec);
        ClipTranscoder.Check(ffmpeg.avcodec_parameters_to_context(decoder, v->codecpar), "Preparing the decoder");
        decoder->pkt_timebase = v->time_base;
        if (device != null)
            decoder->hw_device_ctx = ffmpeg.av_buffer_ref(device);
        else
            decoder->thread_count = 0;
        int opened = ffmpeg.avcodec_open2(decoder, codec, null);
        if (opened < 0)
        {
            ffmpeg.avcodec_free_context(&decoder);
            if (device != null)
            {
                Return(device);
                device = null;
                throw new HardwareDecodeFailedException(ClipTranscoder.ErrorText(opened));
            }
            ClipTranscoder.Check(opened, $"Opening the {ffmpeg.avcodec_get_name(id)} decoder");
        }
        return decoder;
    }

    static bool SupportsD3D11(AVCodec* codec)
    {
        for (int i = 0; ; i++)
        {
            AVCodecHWConfig* config = ffmpeg.avcodec_get_hw_config(codec, i);
            if (config == null)
                return false;
            if (config->device_type == AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA &&
                (config->methods & 0x01) != 0) // AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX
                return true;
        }
    }
}
