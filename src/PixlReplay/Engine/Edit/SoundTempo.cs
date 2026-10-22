using FFmpeg.AutoGen;
using PixlReplay.Engine.Share;

namespace PixlReplay.Engine.Edit;

/// <summary>
/// Plays sound faster or slower at the same pitch (FFmpeg's atempo, as the export does), for the preview of a part at
/// another speed: 48 kHz stereo 16-bit in, the same out. Not thread-safe: one caller.
/// </summary>
public sealed unsafe class SoundTempo : IDisposable
{
    const int Rate = 48_000, Channels = 2, FrameBytes = 4;

    AVFilterGraph* _graph;
    readonly AVFilterContext* _source;
    readonly AVFilterContext* _sink;
    AVFrame* _out;
    long _pts;
    bool _flushed;

    public SoundTempo(double factor)
    {
        ClipTranscoder.RequireLoaded();
        _graph = ffmpeg.avfilter_graph_alloc();
        _graph->nb_threads = 1;
        string args = $"time_base=1/{Rate}:sample_rate={Rate}:sample_fmt=s16:channel_layout=stereo";
        string text = EditGraph.TempoChain(factor) + $",aformat=sample_fmts=s16:sample_rates={Rate}:channel_layouts=stereo";
        FilterChain.Build(_graph, sound: true, args, text, "Sound speed", out _source, out _sink);
        _out = ffmpeg.av_frame_alloc();
    }

    /// <summary>Takes a piece of sound and returns what is ready at the new speed (atempo holds a little back).</summary>
    public byte[] Push(ReadOnlySpan<byte> pcm)
    {
        int samples = pcm.Length / FrameBytes;
        if (samples == 0 || _flushed)
            return [];
        AVFrame* frame = ffmpeg.av_frame_alloc();
        try
        {
            frame->format = (int)AVSampleFormat.AV_SAMPLE_FMT_S16;
            frame->sample_rate = Rate;
            ffmpeg.av_channel_layout_default(&frame->ch_layout, Channels);
            frame->nb_samples = samples;
            ClipTranscoder.Check(ffmpeg.av_frame_get_buffer(frame, 0), "Preparing the sound");
            pcm[..(samples * FrameBytes)].CopyTo(new Span<byte>(frame->data[0], samples * FrameBytes));
            frame->pts = _pts;
            _pts += samples;
            ClipTranscoder.Check(ffmpeg.av_buffersrc_add_frame_flags(_source, frame, 0), "Sound speed");
        }
        finally
        {
            ffmpeg.av_frame_free(&frame);
        }
        return Drain();
    }

    /// <summary>The sound still held back, at the end of the part.</summary>
    public byte[] Flush()
    {
        if (_flushed)
            return [];
        _flushed = true;
        ClipTranscoder.Check(ffmpeg.av_buffersrc_add_frame_flags(_source, null, 0), "Sound speed");
        return Drain();
    }

    byte[] Drain()
    {
        var result = new List<byte>();
        while (true)
        {
            int got = ffmpeg.av_buffersink_get_frame(_sink, _out);
            if (got == ffmpeg.AVERROR(ffmpeg.EAGAIN) || got == ffmpeg.AVERROR_EOF)
                break;
            ClipTranscoder.Check(got, "Sound speed");
            result.AddRange(new ReadOnlySpan<byte>(_out->data[0], _out->nb_samples * FrameBytes).ToArray());
            ffmpeg.av_frame_unref(_out);
        }
        return [.. result];
    }

    public void Dispose()
    {
        AVFrame* frame = _out;
        ffmpeg.av_frame_free(&frame);
        _out = null;
        AVFilterGraph* graph = _graph;
        if (graph != null)
            ffmpeg.avfilter_graph_free(&graph);
        _graph = null;
    }
}
