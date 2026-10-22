using FFmpeg.AutoGen;

namespace PixlReplay.Engine.Share;

/// <summary>A picture from the clip as 32-bit BGRA rows (top-down, no padding).</summary>
public sealed record GrabbedFrame(int Width, int Height, byte[] Bgra);

/// <summary>
/// Reads single frames of a clip for the trim bar and the preview. Keeps the file and decoder open between calls
/// (one grabber per open share panel). Uses the graphics card like the converter (AV1 needs it), else the CPU.
/// Not thread-safe: one caller at a time.
/// </summary>
public sealed unsafe class FrameGrabber : IDisposable
{
    const int SwsArea = 0x20;      // SWS_AREA: clean downscaling
    const int SwsBilinear = 0x2;

    AVFormatContext* _format;
    AVCodecContext* _decoder;
    AVBufferRef* _device;
    AVFrame* _frame;
    AVFrame* _transfer;
    AVPacket* _packet;
    SwsContext* _sws;
    (int W, int H, AVPixelFormat Fmt, int OutW, int OutH) _swsKey;
    readonly int _video;
    readonly AVRational _timeBase;
    readonly long _startPts;

    /// <summary>Pictures decoded so far (tests: a step forward must not start over from the key frame).</summary>
    internal long FramesDecoded { get; private set; }

    /// <summary>The time of the picture still in _frame (the last one given), or none.</summary>
    long _at = long.MinValue;
    /// <summary>The decoder was told the clip ended: it must be seeked before it decodes again.</summary>
    bool _drained;

    public FrameGrabber(string path)
    {
        if (!File.Exists(path))
            throw new ShareException($"The clip was not found: {path}");
        ClipTranscoder.RequireLoaded();
        AVFormatContext* format = null;
        ClipTranscoder.Check(ffmpeg.avformat_open_input(&format, path, null, null), "Opening the clip");
        _format = format;
        try
        {
            ClipTranscoder.Check(ffmpeg.avformat_find_stream_info(format, null), "Reading the clip");
            _video = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            if (_video < 0)
                throw new ShareException("The clip has no video.");
            AVStream* stream = format->streams[_video];
            _timeBase = stream->time_base;
            _startPts = stream->start_time == ffmpeg.AV_NOPTS_VALUE ? 0 : stream->start_time;
            _decoder = HardwareDecoding.OpenVideo(stream, hardwareDecode: true, out _device);
            _frame = ffmpeg.av_frame_alloc();
            _transfer = ffmpeg.av_frame_alloc();
            _packet = ffmpeg.av_packet_alloc();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// The frame at <paramref name="seconds"/>, <paramref name="width"/> pixels wide (height follows the clip's shape,
    /// even). <paramref name="exact"/> false returns the key frame at or before that time, which needs no decoding past
    /// it (fast enough for the trim bar while dragging). Null if nothing could be decoded there.
    /// </summary>
    public GrabbedFrame? Grab(double seconds, int width, bool exact)
    {
        long target = _startPts + (long)(Math.Max(0, seconds) / ffmpeg.av_q2d(_timeBase));
        // The same moment again (an edit while the playhead stays): the picture is still here, only converted again.
        if (exact && _at == target)
            return ConvertKept(width, SwsBilinear);
        // A little ahead of the last picture (stepping, a short drag): decoding on from it costs less than going back to
        // the key frame and decoding up to here again, and gives the very same picture. Anything else seeks as before.
        long ahead = (long)(AheadSeconds / ffmpeg.av_q2d(_timeBase));
        bool onward = exact && !_drained && _at != long.MinValue && target > _at && target - _at <= ahead;
        if (!onward)
        {
            if (ffmpeg.avformat_seek_file(_format, _video, long.MinValue, target, target, 0) < 0)
                ffmpeg.avformat_seek_file(_format, _video, long.MinValue, target, long.MaxValue, 0);
            ffmpeg.avcodec_flush_buffers(_decoder);
            _drained = false;
        }
        ffmpeg.av_frame_unref(_frame);
        _at = long.MinValue;

        AVFrame* best = null;
        bool eof = false;
        for (int packets = 0; packets < 2000 && best == null; packets++)
        {
            int read = ffmpeg.av_read_frame(_format, _packet);
            if (read < 0)
            {
                eof = true;
                _drained = true;
                ffmpeg.avcodec_send_packet(_decoder, null);
            }
            else if (_packet->stream_index != _video)
            {
                ffmpeg.av_packet_unref(_packet);
                continue;
            }
            else
            {
                ffmpeg.avcodec_send_packet(_decoder, _packet);
                ffmpeg.av_packet_unref(_packet);
            }

            while (true)
            {
                int got = ffmpeg.avcodec_receive_frame(_decoder, _frame);
                if (got < 0)
                    break;
                FramesDecoded++;
                long pts = _frame->best_effort_timestamp == ffmpeg.AV_NOPTS_VALUE ? _frame->pts : _frame->best_effort_timestamp;
                // Fast: the first picture is the key frame. Exact: the first picture at or past the time; near the end of
                // the clip the last picture there is.
                if (!exact || pts >= target || eof)
                {
                    best = _frame;
                    break;
                }
                ffmpeg.av_frame_unref(_frame);
            }
            if (eof && best == null)
                break;
        }
        if (best == null)
            return null;
        // The picture stays decoded in _frame for the next grab (the same moment, or a step on from it).
        _at = exact ? (best->best_effort_timestamp == ffmpeg.AV_NOPTS_VALUE ? best->pts : best->best_effort_timestamp) : long.MinValue;
        return ConvertKept(width, exact ? SwsBilinear : SwsArea);
    }

    /// <summary>How far ahead of the last picture a grab decodes on instead of seeking (about half a key frame interval).</summary>
    const double AheadSeconds = 0.5;

    GrabbedFrame ConvertKept(int width, int flags)
    {
        try
        {
            return Convert(_frame, width, flags);
        }
        finally
        {
            ffmpeg.av_frame_unref(_transfer);
        }
    }

    GrabbedFrame Convert(AVFrame* frame, int width, int flags)
    {
        AVFrame* picture = frame;
        if (frame->hw_frames_ctx != null)
        {
            ClipTranscoder.Check(ffmpeg.av_hwframe_transfer_data(_transfer, frame, 0), "Reading the decoded picture");
            picture = _transfer;
        }
        int outW = Math.Max(2, width & ~1);
        int outH = Math.Max(2, (int)Math.Round((double)outW * picture->height / picture->width) & ~1);
        var key = (picture->width, picture->height, (AVPixelFormat)picture->format, outW, outH);
        if (_sws == null || _swsKey != key)
        {
            ffmpeg.sws_freeContext(_sws);
            _sws = ffmpeg.sws_getContext(picture->width, picture->height, (AVPixelFormat)picture->format, outW, outH,
                AVPixelFormat.AV_PIX_FMT_BGRA, flags, null, null, null);
            _swsKey = key;
            if (_sws == null)
                throw new ShareException("Cannot convert the picture.");
        }
        var bgra = new byte[outW * outH * 4];
        fixed (byte* dst = bgra)
        {
            byte*[] dstData = [dst, null, null, null];
            int[] dstStride = [outW * 4, 0, 0, 0];
            byte*[] srcData = [picture->data[0], picture->data[1], picture->data[2], picture->data[3]];
            int[] srcStride = [picture->linesize[0], picture->linesize[1], picture->linesize[2], picture->linesize[3]];
            ffmpeg.sws_scale(_sws, srcData, srcStride, 0, picture->height, dstData, dstStride);
        }
        // The picture has no alpha; make it opaque for display.
        for (int i = 3; i < bgra.Length; i += 4)
            bgra[i] = 255;
        return new GrabbedFrame(outW, outH, bgra);
    }

    public void Dispose()
    {
        ffmpeg.sws_freeContext(_sws);
        _sws = null;
        AVFrame* f = _frame;
        ffmpeg.av_frame_free(&f);
        f = _transfer;
        ffmpeg.av_frame_free(&f);
        _frame = _transfer = null;
        AVPacket* p = _packet;
        ffmpeg.av_packet_free(&p);
        _packet = null;
        AVCodecContext* d = _decoder;
        if (d != null)
            ffmpeg.avcodec_free_context(&d);
        _decoder = null;
        HardwareDecoding.Return(_device);
        _device = null;
        AVFormatContext* format = _format;
        if (format != null)
            ffmpeg.avformat_close_input(&format);
        _format = null;
    }
}
