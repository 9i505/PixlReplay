using FFmpeg.AutoGen;

namespace PixlReplay.Engine.Share;

/// <summary>
/// An FFmpeg filter graph from one source to one sink (the converter, the editor's export, its looks and the sound speed
/// all build theirs this way): the filter text runs between a pad named <c>in</c> and one named <c>out</c> (or the names given).
/// </summary>
static unsafe class FilterChain
{
    /// <summary>
    /// Fills <paramref name="graph"/>: a picture (or <paramref name="sound"/>) source made with <paramref name="args"/>, then
    /// <paramref name="text"/>, then a sink. <paramref name="what"/> names the filters in an error.
    /// </summary>
    public static void Build(AVFilterGraph* graph, bool sound, string args, string text, string what,
        out AVFilterContext* source, out AVFilterContext* sink, string inName = "in", string outName = "out")
    {
        AVFilterContext* first = null, last = null;
        ClipTranscoder.Check(ffmpeg.avfilter_graph_create_filter(&first, ffmpeg.avfilter_get_by_name(sound ? "abuffer" : "buffer"), inName, args, null, graph),
            sound ? "Sound input" : "Picture input");
        ClipTranscoder.Check(ffmpeg.avfilter_graph_create_filter(&last, ffmpeg.avfilter_get_by_name(sound ? "abuffersink" : "buffersink"), outName, null, null, graph),
            sound ? "Sound output" : "Picture output");
        source = first;
        sink = last;
        AVFilterInOut* outputs = ffmpeg.avfilter_inout_alloc();
        AVFilterInOut* inputs = ffmpeg.avfilter_inout_alloc();
        try
        {
            outputs->name = ffmpeg.av_strdup(inName);
            outputs->filter_ctx = first;
            outputs->pad_idx = 0;
            outputs->next = null;
            inputs->name = ffmpeg.av_strdup(outName);
            inputs->filter_ctx = last;
            inputs->pad_idx = 0;
            inputs->next = null;
            ClipTranscoder.Check(ffmpeg.avfilter_graph_parse_ptr(graph, text, &inputs, &outputs, null), what);
            ClipTranscoder.Check(ffmpeg.avfilter_graph_config(graph, null), what);
        }
        finally
        {
            ffmpeg.avfilter_inout_free(&inputs);
            ffmpeg.avfilter_inout_free(&outputs);
        }
    }
}
