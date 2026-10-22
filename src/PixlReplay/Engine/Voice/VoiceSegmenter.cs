namespace PixlReplay.Engine.Voice;

/// <summary>
/// Turns 16 kHz audio chunks plus a speech probability per chunk into the stretches worth checking.
/// Listening (with context): whenever the speaker pauses, the last three seconds of audio, so a phrase
/// with a pause in the middle ("PixlReplay, ... clip it") arrives whole; while someone keeps talking, the last
/// three seconds once a second. Enrolling (no context, longer hangover): each utterance on its own.
/// </summary>
public sealed class VoiceSegmenter
{
    public const int Chunk = 512;                       // 32 ms, Silero VAD's frame
    public const float StartProbability = 0.5f;
    public const float EndProbability = 0.35f;
    public const int PreRollChunks = 10;                // ~0.3 s before speech starts
    public const int HangoverChunks = 10;               // ~0.3 s of quiet ends an utterance
    public const int EnrollHangoverChunks = 25;         // ~0.8 s, so a comma pause stays inside a sample
    public const int WindowChunks = 94;                 // ~3 s
    public const int StepChunks = 31;                   // ~1 s

    readonly int _hangover;
    readonly bool _context;
    readonly Queue<float[]> _preRoll = new();
    readonly Queue<float[]> _speech = new();
    readonly Queue<float[]> _recent = new();            // last ~3 s of all audio
    bool _inSpeech;
    int _quiet, _total;

    public VoiceSegmenter(int hangoverChunks = HangoverChunks, bool withContext = true)
    {
        _hangover = hangoverChunks;
        _context = withContext;
    }

    public List<float[]> Push(ReadOnlySpan<float> chunk, float probability)
    {
        var found = new List<float[]>();
        var copy = chunk.ToArray();
        Remember(_recent, copy);
        if (!_inSpeech)
        {
            if (probability >= StartProbability)
            {
                _inSpeech = true;
                _quiet = 0;
                _total = 0;
                foreach (var c in _preRoll)
                    Keep(c);
                _preRoll.Clear();
                Keep(copy);
            }
            else
            {
                _preRoll.Enqueue(copy);
                if (_preRoll.Count > PreRollChunks)
                    _preRoll.Dequeue();
            }
            return found;
        }

        Keep(copy);
        _quiet = probability < EndProbability ? _quiet + 1 : 0;
        if (_quiet >= _hangover)
        {
            found.Add(Join(_context ? _recent : _speech));
            _inSpeech = false;
            _speech.Clear();
            return found;
        }
        if (_quiet == 0 && _total >= WindowChunks && (_total - WindowChunks) % StepChunks == 0)
            found.Add(Join(_context ? _recent : _speech));
        return found;
    }

    void Keep(float[] chunk)
    {
        Remember(_speech, chunk);
        _total++;
    }

    static void Remember(Queue<float[]> queue, float[] chunk)
    {
        queue.Enqueue(chunk);
        if (queue.Count > WindowChunks)
            queue.Dequeue();
    }

    static float[] Join(Queue<float[]> chunks)
    {
        var all = new float[chunks.Count * Chunk];
        int i = 0;
        foreach (var c in chunks)
            c.CopyTo(all, i++ * Chunk);
        return all;
    }
}
