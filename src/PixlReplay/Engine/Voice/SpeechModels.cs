using Microsoft.ML.OnnxRuntime;

namespace PixlReplay.Engine.Voice;

/// <summary>Silero VAD state for one audio stream (the model is recurrent).</summary>
public sealed class VadState
{
    internal readonly float[] H = new float[2 * 64];
    internal readonly float[] C = new float[2 * 64];
}

/// <summary>
/// The three small ONNX models behind Smart Save, run on one CPU thread:
/// Silero VAD (is someone talking?) and openWakeWord's mel front end + Google speech embedding
/// (a 96-number fingerprint of every 80 ms, robust to noise and voice changes).
/// </summary>
public sealed class SpeechModels : IDisposable
{
    public const int SampleRate = 16000;
    const int MelWindow = 76, MelStep = 8, MelBands = 32, EmbeddingSize = 96;
    static readonly string[] Files = ["melspectrogram.onnx", "embedding_model.onnx", "silero_vad.onnx"];

    readonly InferenceSession? _mel, _embedding;
    readonly InferenceSession _vad;
    readonly RunOptions _run = new();
    readonly OrtValue _sampleRate = OrtValue.CreateTensorValueFromMemory(new long[] { SampleRate }, []);

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "models");

    public static bool Available(string directory) => Files.All(f => File.Exists(Path.Combine(directory, f)));

    /// <param name="vadOnly">Only the speech detector (finding speech for captions): the phrase models are not loaded.</param>
    public SpeechModels(string directory, bool vadOnly = false)
    {
        using var options = new SessionOptions
        {
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };
        if (!vadOnly)
        {
            _mel = new InferenceSession(Path.Combine(directory, Files[0]), options);
            _embedding = new InferenceSession(Path.Combine(directory, Files[1]), options);
        }
        _vad = new InferenceSession(Path.Combine(directory, Files[2]), options);
    }

    public VadState NewVad() => new();

    /// <summary>The detector's input, filled for each question (one thread asks at a time, like the rest of this class).</summary>
    readonly float[] _vadInput = new float[512];

    /// <summary>Probability (0..1) that a 512-sample chunk of 16 kHz audio is speech.</summary>
    public float SpeechProbability(VadState state, ReadOnlySpan<float> chunk)
    {
        float[] sound = chunk.Length == _vadInput.Length ? _vadInput : new float[chunk.Length];
        chunk.CopyTo(sound);
        using var input = OrtValue.CreateTensorValueFromMemory(sound, [1, chunk.Length]);
        using var h = OrtValue.CreateTensorValueFromMemory(state.H, [2, 1, 64]);
        using var c = OrtValue.CreateTensorValueFromMemory(state.C, [2, 1, 64]);
        using var outputs = _vad.Run(_run, ["input", "sr", "h", "c"], [input, _sampleRate, h, c], ["output", "hn", "cn"]);
        outputs[1].GetTensorDataAsSpan<float>().CopyTo(state.H);
        outputs[2].GetTensorDataAsSpan<float>().CopyTo(state.C);
        return outputs[0].GetTensorDataAsSpan<float>()[0];
    }

    /// <summary>One 96-number embedding per 80 ms of <paramref name="audio"/> (16 kHz), padded with 0.4 s of silence each side.</summary>
    public float[][] Embed(ReadOnlySpan<float> audio)
    {
        if (_mel == null || _embedding == null)
            throw new InvalidOperationException("Loaded with the speech detector only.");
        int pad = (int)(0.4 * SampleRate);
        var x = new float[audio.Length + 2 * pad];
        for (int i = 0; i < audio.Length; i++)
            x[pad + i] = audio[i] * 32767f;

        float[] mel;
        int frames;
        using (var input = OrtValue.CreateTensorValueFromMemory(x, [1, x.Length]))
        using (var outputs = _mel.Run(_run, ["input"], [input], ["output"]))
        {
            var shape = outputs[0].GetTensorTypeAndShape().Shape;
            frames = (int)shape[2];
            mel = outputs[0].GetTensorDataAsSpan<float>().ToArray();
        }
        for (int i = 0; i < mel.Length; i++)
            mel[i] = mel[i] / 10f + 2f;
        if (frames < MelWindow)
        {
            float floor = mel.Length > 0 ? mel.Min() : 0;
            var padded = Enumerable.Repeat(floor, MelWindow * MelBands).ToArray();
            mel.CopyTo(padded, 0);
            mel = padded;
            frames = MelWindow;
        }

        int windows = (frames - MelWindow) / MelStep + 1;
        var batch = new float[windows * MelWindow * MelBands];
        for (int w = 0; w < windows; w++)
            Array.Copy(mel, w * MelStep * MelBands, batch, w * MelWindow * MelBands, MelWindow * MelBands);
        using var batchValue = OrtValue.CreateTensorValueFromMemory(batch, [windows, MelWindow, MelBands, 1]);
        using var embOut = _embedding.Run(_run, ["input_1"], [batchValue], ["conv2d_19"]);
        var flat = embOut[0].GetTensorDataAsSpan<float>();
        var result = new float[windows][];
        for (int w = 0; w < windows; w++)
            result[w] = flat.Slice(w * EmbeddingSize, EmbeddingSize).ToArray();
        return result;
    }

    public void Dispose()
    {
        _mel?.Dispose();
        _embedding?.Dispose();
        _vad.Dispose();
        _sampleRate.Dispose();
        _run.Dispose();
    }
}
