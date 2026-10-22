using FFmpeg.AutoGen;

namespace PixlReplay.Engine.Share;

/// <summary>
/// Decodes a clip for the share panel's player. From a point on it hands out pictures at a fixed size (BGRA, converted
/// only when asked, so a late one costs little) and the first sound track (the full mix) as 16-bit stereo at 48 kHz,
/// trimmed to start exactly at that point. Times are seconds into the clip, the trim's clock.
/// Pictures and sound are read apart (the file is opened twice, each reading only its own track), so the caller can
/// keep the sound ahead however the file stores them: many recorders write a second or more of pictures, then that
/// second's sound. The caller paces it. Not thread-safe.
/// </summary>
public sealed unsafe class ClipPlayback : IDisposable
{
    public const int SampleRate = 48_000;
    /// <summary>Bytes per sound sample: two 16-bit channels.</summary>
    public const int BytesPerFrame = 4;
    const int SwsBilinear = 0x2;

    AVFormatContext* _format;
    /// <summary>The same file again, for the sound alone.</summary>
    AVFormatContext* _audioFormat;
    AVCodecContext* _video;
    AVCodecContext* _audio;
    AVBufferRef* _device;
    AVFrame* _picture;
    AVFrame* _transfer;
    AVFrame* _samples;
    AVPacket* _packet;
    AVPacket* _audioPacket;
    SwsContext* _sws;
    (int W, int H, AVPixelFormat Fmt) _swsKey;
    SwrContext* _swr;
    readonly int _videoIndex, _audioIndex = -1;
    readonly AVRational _videoBase, _audioBase;
    /// <summary>Clip time 0: the video stream's first timestamp, in seconds.</summary>
    readonly double _origin;
    readonly double _frameSeconds;
    readonly int _width, _height;
    double _from;
    /// <summary>Time of the next sound sample once sound has started (pieces follow on without gaps).</summary>
    double _soundAt = double.NaN;
    byte[] _sound = [];
    int _soundLength;
    bool _videoEof, _audioEof, _videoDone, _audioDone;

    /// <param name="width">Picture width in pixels (even).</param>
    /// <param name="height">Picture height in pixels (even).</param>
    public ClipPlayback(string path, int width, int height, int audioTrack = 0)
    {
        if (!File.Exists(path))
            throw new ShareException($"The clip was not found: {path}");
        _width = Math.Max(2, width & ~1);
        _height = Math.Max(2, height & ~1);
        ClipTranscoder.RequireLoaded();
        AVFormatContext* format = null;
        ClipTranscoder.Check(ffmpeg.avformat_open_input(&format, path, null, null), "Opening the clip");
        _format = format;
        try
        {
            ClipTranscoder.Check(ffmpeg.avformat_find_stream_info(format, null), "Reading the clip");
            _videoIndex = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            if (_videoIndex < 0)
                throw new ShareException("The clip has no video.");
            AVStream* video = format->streams[_videoIndex];
            _videoBase = video->time_base;
            _origin = (video->start_time == ffmpeg.AV_NOPTS_VALUE ? 0 : video->start_time) * ffmpeg.av_q2d(_videoBase);
            AVRational rate = video->avg_frame_rate.num > 0 ? video->avg_frame_rate : video->r_frame_rate;
            _frameSeconds = rate.num > 0 ? rate.den / (double)rate.num : 1.0 / 60;
            _video = HardwareDecoding.OpenVideo(video, hardwareDecode: true, out _device);

            // The first sound track is the full mix (like the converter uses).
            for (int i = 0, seen = 0; i < format->nb_streams; i++)
            {
                if (format->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO && seen++ == audioTrack)
                {
                    _audioIndex = i;
                    break;
                }
            }
            if (_audioIndex >= 0)
            {
                AVStream* audio = format->streams[_audioIndex];
                _audioBase = audio->time_base;
                AVCodec* codec = ffmpeg.avcodec_find_decoder(audio->codecpar->codec_id);
                if (codec != null)
                {
                    _audio = ffmpeg.avcodec_alloc_context3(codec);
                    ClipTranscoder.Check(ffmpeg.avcodec_parameters_to_context(_audio, audio->codecpar), "Preparing the sound decoder");
                    _audio->pkt_timebase = audio->time_base;
                    ClipTranscoder.Check(ffmpeg.avcodec_open2(_audio, codec, null), "Opening the sound decoder");

                    AVFormatContext* soundFile = null;
                    ClipTranscoder.Check(ffmpeg.avformat_open_input(&soundFile, path, null, null), "Opening the clip");
                    _audioFormat = soundFile;
                    ClipTranscoder.Check(ffmpeg.avformat_find_stream_info(soundFile, null), "Reading the clip");
                    ReadOnly(soundFile, _audioIndex);
                }
            }
            ReadOnly(format, _videoIndex);
            _picture = ffmpeg.av_frame_alloc();
            _transfer = ffmpeg.av_frame_alloc();
            _samples = ffmpeg.av_frame_alloc();
            _packet = ffmpeg.av_packet_alloc();
            _audioPacket = ffmpeg.av_packet_alloc();
            Seek(0);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>The file reads only <paramref name="index"/>'s packets (the demuxer skips the other tracks).</summary>
    static void ReadOnly(AVFormatContext* format, int index)
    {
        for (int i = 0; i < format->nb_streams; i++)
            format->streams[i]->discard = i == index ? AVDiscard.AVDISCARD_DEFAULT : AVDiscard.AVDISCARD_ALL;
    }

    public bool HasAudio => _audio != null;

    /// <summary>Length of one picture in seconds (from the frame rate).</summary>
    public double FrameSeconds => _frameSeconds;

    /// <summary>Time of the picture <see cref="NextPicture"/> gave.</summary>
    public double PictureTime { get; private set; }

    /// <summary>Time of the sound piece <see cref="NextSound"/> gave.</summary>
    public double SoundTime { get; private set; }

    /// <summary>The sound piece <see cref="NextSound"/> gave (valid until the next call).</summary>
    public ReadOnlySpan<byte> Sound => _sound.AsSpan(0, _soundLength);

    /// <summary>Starts over at <paramref name="seconds"/> into the clip.</summary>
    public void Seek(double seconds)
    {
        _from = Math.Max(0, seconds);
        long target = (long)((_origin + _from) / ffmpeg.av_q2d(_videoBase));
        if (ffmpeg.avformat_seek_file(_format, _videoIndex, long.MinValue, target, target, 0) < 0)
            ffmpeg.avformat_seek_file(_format, _videoIndex, long.MinValue, target, long.MaxValue, 0);
        ffmpeg.avcodec_flush_buffers(_video);
        if (_audio != null)
        {
            long soundTarget = (long)((_origin + _from) / ffmpeg.av_q2d(_audioBase));
            if (ffmpeg.avformat_seek_file(_audioFormat, _audioIndex, long.MinValue, soundTarget, soundTarget, 0) < 0)
                ffmpeg.avformat_seek_file(_audioFormat, _audioIndex, long.MinValue, soundTarget, long.MaxValue, 0);
            ffmpeg.avcodec_flush_buffers(_audio);
        }
        SwrContext* swr = _swr;
        ffmpeg.swr_free(&swr);
        _swr = null;
        ffmpeg.av_frame_unref(_picture);
        _soundAt = double.NaN;
        _soundLength = 0;
        _videoEof = _audioEof = false;
        _videoDone = false;
        _audioDone = _audio == null;
    }

    /// <summary>
    /// The next picture at or after the seek point (converted only by <see cref="CopyPicture"/>); false when the clip
    /// has no more.
    /// </summary>
    public bool NextPicture()
    {
        ffmpeg.av_frame_unref(_picture);
        while (!_videoDone)
        {
            if (ReceivePicture())
                return true;
            if (_videoEof)
            {
                _videoDone = true; // flushed, and nothing more came
                break;
            }
            if (!Feed(_format, _packet, _videoIndex, _video))
                _videoEof = true;
        }
        return false;
    }

    /// <summary>The next sound piece at or after the seek point; false when the clip has no more (or no sound).</summary>
    public bool NextSound()
    {
        while (!_audioDone)
        {
            if (ReceiveSound())
                return true;
            if (_audioEof)
            {
                _audioDone = true;
                break;
            }
            if (!Feed(_audioFormat, _audioPacket, _audioIndex, _audio))
                _audioEof = true;
        }
        return false;
    }

    /// <summary>Hands the decoder the next packet of its track; at the end of the file, tells it so (false).</summary>
    static bool Feed(AVFormatContext* format, AVPacket* packet, int index, AVCodecContext* decoder)
    {
        while (true)
        {
            if (ffmpeg.av_read_frame(format, packet) < 0)
            {
                ffmpeg.avcodec_send_packet(decoder, null);
                return false;
            }
            bool ours = packet->stream_index == index;
            if (ours)
                ffmpeg.avcodec_send_packet(decoder, packet);
            ffmpeg.av_packet_unref(packet);
            if (ours)
                return true;
        }
    }

    /// <summary>A decoded picture showing at or after the seek point (the one on screen at that moment counts).</summary>
    bool ReceivePicture()
    {
        while (true)
        {
            int got = ffmpeg.avcodec_receive_frame(_video, _picture);
            if (got == ffmpeg.AVERROR_EOF)
                _videoDone = true;
            if (got < 0)
                return false;
            long pts = _picture->best_effort_timestamp == ffmpeg.AV_NOPTS_VALUE ? _picture->pts : _picture->best_effort_timestamp;
            double t = pts * ffmpeg.av_q2d(_videoBase) - _origin;
            if (t > _from - _frameSeconds / 2)
            {
                PictureTime = t;
                return true;
            }
            ffmpeg.av_frame_unref(_picture);
        }
    }

    /// <summary>A piece of sound at or after the seek point, as 16-bit stereo at 48 kHz.</summary>
    bool ReceiveSound()
    {
        while (true)
        {
            int got = ffmpeg.avcodec_receive_frame(_audio, _samples);
            if (got == ffmpeg.AVERROR_EOF)
                _audioDone = true;
            if (got < 0)
                return false;
            try
            {
                if (Convert(_samples))
                    return true;
            }
            finally
            {
                ffmpeg.av_frame_unref(_samples);
            }
        }
    }

    bool Convert(AVFrame* frame)
    {
        if (_swr == null)
        {
            AVChannelLayout stereo;
            ffmpeg.av_channel_layout_default(&stereo, 2);
            SwrContext* swr = null;
            ClipTranscoder.Check(ffmpeg.swr_alloc_set_opts2(&swr, &stereo, AVSampleFormat.AV_SAMPLE_FMT_S16, SampleRate,
                &frame->ch_layout, (AVSampleFormat)frame->format, frame->sample_rate, 0, null), "Preparing the sound");
            _swr = swr;
            ClipTranscoder.Check(ffmpeg.swr_init(_swr), "Preparing the sound");
        }
        int room = ffmpeg.swr_get_out_samples(_swr, frame->nb_samples);
        if (_sound.Length < room * BytesPerFrame)
            _sound = new byte[room * BytesPerFrame * 2];
        int samples;
        fixed (byte* outData = _sound)
        {
            byte* outPtr = outData;
            samples = ffmpeg.swr_convert(_swr, &outPtr, room, frame->extended_data, frame->nb_samples);
        }
        if (samples <= 0)
            return false;

        double start = _soundAt;
        if (double.IsNaN(start))
        {
            long pts = frame->best_effort_timestamp == ffmpeg.AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
            start = pts * ffmpeg.av_q2d(_audioBase) - _origin;
        }
        double end = start + samples / (double)SampleRate;
        if (end <= _from)
        {
            // Before the seek point: dropped, but the clock follows it.
            _soundAt = end;
            return false;
        }
        _soundLength = samples * BytesPerFrame;
        if (start < _from)
        {
            // Starts before the seek point: cut its head off so the sound begins exactly there.
            int skip = Math.Min(samples, (int)Math.Round((_from - start) * SampleRate)) * BytesPerFrame;
            Buffer.BlockCopy(_sound, skip, _sound, 0, _soundLength - skip);
            _soundLength -= skip;
            start = _from;
        }
        else if (double.IsNaN(_soundAt) && start > _from + 0.0005)
        {
            // The first sound comes a little after the seek point: silence before it keeps it in step with the pictures.
            int pad = (int)Math.Round((start - _from) * SampleRate) * BytesPerFrame;
            var padded = new byte[Math.Max(_sound.Length, pad + _soundLength)];
            Buffer.BlockCopy(_sound, 0, padded, pad, _soundLength);
            _sound = padded;
            _soundLength += pad;
            start = _from;
        }
        _soundAt = end;
        SoundTime = start;
        return _soundLength > 0;
    }

    /// <summary>Converts the picture <see cref="NextPicture"/> gave into <paramref name="bgra"/> (width × height × 4 bytes).</summary>
    public void CopyPicture(Span<byte> bgra)
    {
        if (bgra.Length < _width * _height * 4)
            throw new ArgumentException("buffer too small", nameof(bgra));
        AVFrame* picture = _picture;
        if (picture->hw_frames_ctx != null)
        {
            ClipTranscoder.Check(ffmpeg.av_hwframe_transfer_data(_transfer, _picture, 0), "Reading the decoded picture");
            picture = _transfer;
        }
        try
        {
            var key = (picture->width, picture->height, (AVPixelFormat)picture->format);
            if (_sws == null || _swsKey != key)
            {
                ffmpeg.sws_freeContext(_sws);
                _sws = ffmpeg.sws_getContext(picture->width, picture->height, (AVPixelFormat)picture->format, _width, _height,
                    AVPixelFormat.AV_PIX_FMT_BGRA, SwsBilinear, null, null, null);
                _swsKey = key;
                if (_sws == null)
                    throw new ShareException("Cannot convert the picture.");
            }
            fixed (byte* dst = bgra)
            {
                byte*[] dstData = [dst, null, null, null];
                int[] dstStride = [_width * 4, 0, 0, 0];
                byte*[] srcData = [picture->data[0], picture->data[1], picture->data[2], picture->data[3]];
                int[] srcStride = [picture->linesize[0], picture->linesize[1], picture->linesize[2], picture->linesize[3]];
                ffmpeg.sws_scale(_sws, srcData, srcStride, 0, picture->height, dstData, dstStride);
            }
        }
        finally
        {
            ffmpeg.av_frame_unref(_transfer);
        }
    }

    public void Dispose()
    {
        ffmpeg.sws_freeContext(_sws);
        _sws = null;
        SwrContext* swr = _swr;
        ffmpeg.swr_free(&swr);
        _swr = null;
        AVFrame* f = _picture;
        ffmpeg.av_frame_free(&f);
        f = _transfer;
        ffmpeg.av_frame_free(&f);
        f = _samples;
        ffmpeg.av_frame_free(&f);
        _picture = _transfer = _samples = null;
        AVPacket* p = _packet;
        ffmpeg.av_packet_free(&p);
        _packet = null;
        p = _audioPacket;
        ffmpeg.av_packet_free(&p);
        _audioPacket = null;
        AVCodecContext* c = _video;
        if (c != null)
            ffmpeg.avcodec_free_context(&c);
        c = _audio;
        if (c != null)
            ffmpeg.avcodec_free_context(&c);
        _video = _audio = null;
        HardwareDecoding.Return(_device);
        _device = null;
        AVFormatContext* format = _format;
        if (format != null)
            ffmpeg.avformat_close_input(&format);
        _format = null;
        format = _audioFormat;
        if (format != null)
            ffmpeg.avformat_close_input(&format);
        _audioFormat = null;
    }
}
