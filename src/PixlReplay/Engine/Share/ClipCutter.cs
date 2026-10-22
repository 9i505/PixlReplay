using FFmpeg.AutoGen;

namespace PixlReplay.Engine.Share;

/// <summary>
/// Copies part of a clip as it is: the same video and the first sound track (the full mix), no conversion, so it
/// takes about as long as the disk needs. The part starts on the key frame at or before the asked start (PixlReplay's
/// clips have one every second). Written via a .part file, with the index first so it plays while downloading.
/// </summary>
public static unsafe class ClipCutter
{
    /// <param name="movflags">How the MP4 is laid out ("+faststart": index first; tests also write fragmented files).</param>
    public static ShareResult Cut(string source, string target, double start, double end, IProgress<double>? progress, CancellationToken ct,
        string movflags = "+faststart")
    {
        ClipTranscoder.RequireLoaded();
        if (!File.Exists(source))
            throw new ShareException($"The clip was not found: {source}");
        string temp = $"{target}.{Guid.NewGuid():N}.part";
        AVFormatContext* input = null;
        AVFormatContext* output = null;
        AVPacket* packet = ffmpeg.av_packet_alloc();
        try
        {
            ClipTranscoder.Check(ffmpeg.avformat_open_input(&input, source, null, null), "Opening the clip");
            ClipTranscoder.Check(ffmpeg.avformat_find_stream_info(input, null), "Reading the clip");
            int video = ffmpeg.av_find_best_stream(input, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            if (video < 0)
                throw new ShareException("The clip has no video.");
            int audio = -1;
            for (int i = 0; i < input->nb_streams && audio < 0; i++)
            {
                if (input->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                    audio = i;
            }

            ClipTranscoder.Check(ffmpeg.avformat_alloc_output_context2(&output, null, "mp4", temp), "Preparing the file");
            var map = new Dictionary<int, int>();
            foreach (int index in audio < 0 ? new[] { video } : new[] { video, audio })
            {
                AVStream* outStream = ffmpeg.avformat_new_stream(output, null);
                ClipTranscoder.Check(ffmpeg.avcodec_parameters_copy(outStream->codecpar, input->streams[index]->codecpar), "Preparing the file");
                outStream->codecpar->codec_tag = 0;
                outStream->time_base = input->streams[index]->time_base;
                map[index] = outStream->index;
            }
            ClipTranscoder.Check(ffmpeg.avio_open(&output->pb, temp, ffmpeg.AVIO_FLAG_WRITE), "Creating the file");
            AVDictionary* options = null;
            ffmpeg.av_dict_set(&options, "movflags", movflags, 0);
            try
            {
                ClipTranscoder.Check(ffmpeg.avformat_write_header(output, &options), "Writing the file");
            }
            finally
            {
                ffmpeg.av_dict_free(&options);
            }

            // Back to the key frame at or before the start.
            AVStream* v = input->streams[video];
            long target_ts = (long)(start / ffmpeg.av_q2d(v->time_base)) + (v->start_time == ffmpeg.AV_NOPTS_VALUE ? 0 : v->start_time);
            ffmpeg.avformat_seek_file(input, video, long.MinValue, target_ts, target_ts, 0);

            double origin = double.NaN; // seconds: the first key frame's decode time = 0 in the copy
            bool videoDone = false, audioDone = audio < 0;
            double span = Math.Max(0.001, end - start);
            while (!(videoDone && audioDone))
            {
                ct.ThrowIfCancellationRequested();
                if (ffmpeg.av_read_frame(input, packet) < 0)
                    break;
                try
                {
                    int index = packet->stream_index;
                    if (!map.TryGetValue(index, out int outIndex))
                        continue;
                    AVRational tb = input->streams[index]->time_base;
                    long stamp = packet->pts != ffmpeg.AV_NOPTS_VALUE ? packet->pts : packet->dts;
                    double time = stamp * ffmpeg.av_q2d(tb);
                    if (index == video)
                    {
                        if (double.IsNaN(origin))
                        {
                            if ((packet->flags & ffmpeg.AV_PKT_FLAG_KEY) == 0)
                                continue; // never start between key frames
                            origin = (packet->dts != ffmpeg.AV_NOPTS_VALUE ? packet->dts : packet->pts) * ffmpeg.av_q2d(tb);
                        }
                        if (time >= end)
                        {
                            videoDone = true;
                            continue;
                        }
                        progress?.Report(Math.Clamp((time - start) / span, 0, 1));
                    }
                    else
                    {
                        // Sound from the first picture on, up to the end.
                        if (double.IsNaN(origin) || time < origin)
                            continue;
                        if (time >= end)
                        {
                            audioDone = true;
                            continue;
                        }
                    }
                    long shift = (long)Math.Round(origin / ffmpeg.av_q2d(tb));
                    if (packet->pts != ffmpeg.AV_NOPTS_VALUE)
                        packet->pts -= shift;
                    if (packet->dts != ffmpeg.AV_NOPTS_VALUE)
                        packet->dts -= shift;
                    packet->pos = -1;
                    packet->stream_index = outIndex;
                    ffmpeg.av_packet_rescale_ts(packet, tb, output->streams[outIndex]->time_base);
                    ClipTranscoder.Check(ffmpeg.av_interleaved_write_frame(output, packet), "Writing the file");
                }
                finally
                {
                    ffmpeg.av_packet_unref(packet);
                }
            }
            ClipTranscoder.Check(ffmpeg.av_write_trailer(output), "Finishing the file");
            ffmpeg.avio_closep(&output->pb);
            string final = ClipTranscoder.Place(temp, target);
            progress?.Report(1);
            return new ShareResult(final, new FileInfo(final).Length, "copy");
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
            if (output != null)
            {
                if (output->pb != null)
                    ffmpeg.avio_closep(&output->pb);
                ffmpeg.avformat_free_context(output);
            }
            if (input != null)
                ffmpeg.avformat_close_input(&input);
            ClipTranscoder.TryDelete(temp);
        }
    }
}
