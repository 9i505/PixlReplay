namespace PixlReplay.Engine.Voice;

/// <summary>Minimal 16 kHz mono 16-bit PCM WAV reader and writer for the enrollment samples.</summary>
public static class WavFile
{
    public const int SampleRate = 16000;

    public static float[] Read(string path)
    {
        using var r = new BinaryReader(File.OpenRead(path));
        if (new string(r.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("not a WAV file");
        r.ReadInt32();
        if (new string(r.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("not a WAV file");
        short channels = 1, bits = 16;
        int rate = SampleRate;
        while (r.BaseStream.Position + 8 <= r.BaseStream.Length)
        {
            string id = new(r.ReadChars(4));
            int size = r.ReadInt32();
            long next = r.BaseStream.Position + size + (size & 1);
            if (id == "fmt ")
            {
                r.ReadInt16();
                channels = r.ReadInt16();
                rate = r.ReadInt32();
                r.ReadInt32();
                r.ReadInt16();
                bits = r.ReadInt16();
            }
            else if (id == "data")
            {
                if (bits != 16 || rate != SampleRate)
                    throw new InvalidDataException($"expected 16-bit {SampleRate} Hz, got {bits}-bit {rate} Hz");
                int frames = size / 2 / channels;
                var samples = new float[frames];
                for (int i = 0; i < frames; i++)
                {
                    int sum = 0;
                    for (int c = 0; c < channels; c++)
                        sum += r.ReadInt16();
                    samples[i] = sum / (32768f * channels);
                }
                return samples;
            }
            r.BaseStream.Position = next;
        }
        throw new InvalidDataException("WAV file has no data");
    }

    public static void Write(string path, ReadOnlySpan<float> samples)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8);
        w.Write(36 + samples.Length * 2);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(SampleRate);
        w.Write(SampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples.Length * 2);
        foreach (float s in samples)
            w.Write((short)Math.Clamp(Math.Round(s * 32767), short.MinValue, short.MaxValue));
    }
}
