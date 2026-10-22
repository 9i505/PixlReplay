namespace PixlReplay;

/// <summary>Progress reported on the calling thread at once (<see cref="Progress{T}"/> would post it later).</summary>
internal sealed class InlineProgress(Action<double> report) : IProgress<double>
{
    public void Report(double value) => report(value);
}

/// <summary>Progress of one part, mapped into its share of the whole.</summary>
internal sealed class ScaledProgress(IProgress<double> whole, double from, double to) : IProgress<double>
{
    public void Report(double value) => whole.Report(from + (to - from) * value);
}
