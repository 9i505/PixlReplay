using System.Windows;
using FFmpeg.AutoGen;
using PixlReplay.Engine.Share;

namespace PixlReplay.Engine.Edit;

/// <summary>
/// Runs an FFmpeg filter (a colour look) on single BGRA pictures: the editor's preview frames and edited screenshots,
/// with the same filter text the export uses. The graph is kept between pictures and rebuilt when the text changes.
/// Not thread-safe: one caller at a time.
/// </summary>
public sealed unsafe class FrameFilter : IDisposable
{
    readonly int _width, _height;
    string? _filter;
    AVFilterGraph* _graph;
    AVFilterContext* _source;
    AVFilterContext* _sink;
    AVFrame* _in;
    AVFrame* _out;
    long _pts;

    public FrameFilter(int width, int height, string? filter)
    {
        _width = width;
        _height = height;
        _filter = filter;
    }

    public void SetFilter(string? filter)
    {
        if (filter == _filter)
            return;
        _filter = filter;
        FreeGraph();
    }

    /// <summary>Filters the picture in place (rows top-down, no padding); nothing to do without a filter.</summary>
    public void Apply(Span<byte> bgra)
    {
        if (_filter == null)
            return;
        ClipTranscoder.RequireLoaded();
        if (_graph == null)
            Build();
        ClipTranscoder.Check(ffmpeg.av_frame_make_writable(_in), "Preparing the picture");
        int row = _width * 4;
        for (int y = 0; y < _height; y++)
            bgra.Slice(y * row, row).CopyTo(new Span<byte>(_in->data[0] + y * _in->linesize[0], row));
        _in->pts = _pts++;
        ClipTranscoder.Check(ffmpeg.av_buffersrc_add_frame_flags(_source, _in, 8 /* AV_BUFFERSRC_FLAG_KEEP_REF: the picture stays ours */), "Filtering the picture");
        int got = ffmpeg.av_buffersink_get_frame(_sink, _out);
        if (got < 0)
            return; // a filter that holds pictures back (none of the looks do): leave this one as it was
        for (int y = 0; y < _height; y++)
            new ReadOnlySpan<byte>(_out->data[0] + y * _out->linesize[0], row).CopyTo(bgra.Slice(y * row, row));
        ffmpeg.av_frame_unref(_out);
    }

    void Build()
    {
        _graph = ffmpeg.avfilter_graph_alloc();
        _graph->nb_threads = 1; // small pictures, many calls: no thread start-up each time
        string args = $"video_size={_width}x{_height}:pix_fmt={(int)AVPixelFormat.AV_PIX_FMT_BGRA}:time_base=1/60:pixel_aspect=1/1";
        FilterChain.Build(_graph, sound: false, args, _filter + ",format=bgra", "Colour look", out _source, out _sink);
        _in = ffmpeg.av_frame_alloc();
        _in->width = _width;
        _in->height = _height;
        _in->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
        ClipTranscoder.Check(ffmpeg.av_frame_get_buffer(_in, 0), "Preparing the picture");
        _out = ffmpeg.av_frame_alloc();
    }

    void FreeGraph()
    {
        AVFilterGraph* graph = _graph;
        if (graph != null)
            ffmpeg.avfilter_graph_free(&graph);
        _graph = null;
        _source = _sink = null;
        AVFrame* frame = _in;
        ffmpeg.av_frame_free(&frame);
        frame = _out;
        ffmpeg.av_frame_free(&frame);
        _in = _out = null;
    }

    public void Dispose()
    {
        // Nothing was built (FFmpeg may not even be loaded): nothing to free.
        if (_graph != null || _in != null || _out != null)
            FreeGraph();
    }

    /// <summary>
    /// Blurs <paramref name="box"/> of a BGRA picture in place (a box blur of radius 20 run twice, close to the export's
    /// boxblur=20:2), for the preview of blur items and for screenshots.
    /// </summary>
    public static void Blur(Span<byte> bgra, int width, int height, Int32Rect box)
    {
        int x0 = Math.Clamp(box.X, 0, width), y0 = Math.Clamp(box.Y, 0, height);
        int x1 = Math.Clamp(box.X + box.Width, 0, width), y1 = Math.Clamp(box.Y + box.Height, 0, height);
        int w = x1 - x0, h = y1 - y0;
        if (w <= 1 || h <= 1)
            return;
        var area = new float[w * h * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = ((y0 + y) * width + x0 + x) * 4, d = (y * w + x) * 3;
                area[d] = bgra[s];
                area[d + 1] = bgra[s + 1];
                area[d + 2] = bgra[s + 2];
            }
        var scratch = new float[area.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            BoxPass(area, scratch, w, h, 20, horizontal: true);
            BoxPass(scratch, area, w, h, 20, horizontal: false);
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = (y * w + x) * 3, d = ((y0 + y) * width + x0 + x) * 4;
                bgra[d] = (byte)Math.Clamp(area[s] + 0.5f, 0, 255);
                bgra[d + 1] = (byte)Math.Clamp(area[s + 1] + 0.5f, 0, 255);
                bgra[d + 2] = (byte)Math.Clamp(area[s + 2] + 0.5f, 0, 255);
            }
    }

    /// <summary>One running-sum box blur along rows or columns, edges clamped.</summary>
    static void BoxPass(float[] from, float[] to, int w, int h, int radius, bool horizontal)
    {
        int lines = horizontal ? h : w, length = horizontal ? w : h;
        int r = Math.Min(radius, length - 1);
        float scale = 1f / (2 * r + 1);
        for (int line = 0; line < lines; line++)
        {
            int Index(int i) => horizontal ? (line * w + i) * 3 : (i * w + line) * 3;
            for (int c = 0; c < 3; c++)
            {
                float sum = 0;
                for (int i = -r; i <= r; i++)
                    sum += from[Index(Math.Clamp(i, 0, length - 1)) + c];
                for (int i = 0; i < length; i++)
                {
                    to[Index(i) + c] = sum * scale;
                    sum += from[Index(Math.Min(i + r + 1, length - 1)) + c] - from[Index(Math.Max(i - r, 0)) + c];
                }
            }
        }
    }
}
