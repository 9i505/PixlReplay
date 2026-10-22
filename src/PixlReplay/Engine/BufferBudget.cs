namespace PixlReplay.Engine;

/// <summary>How much RAM the replay buffer may hold.</summary>
public static class BufferBudget
{
    const int FloorMb = 512;

    /// <summary>
    /// The worst-case estimate plus 25% and 64 MB headroom, but never more than a quarter of physical RAM
    /// (with a 512 MB floor). <paramref name="physicalBytes"/> of 0 means unknown.
    /// </summary>
    public static int CapMb(int estimateMb, long physicalBytes)
    {
        int wanted = estimateMb * 5 / 4 + 64;
        if (physicalBytes <= 0)
            return wanted;
        int quarter = (int)Math.Min(int.MaxValue, physicalBytes / 4 / (1024 * 1024));
        return Math.Min(wanted, Math.Max(FloorMb, quarter));
    }

    public static long PhysicalMemoryBytes => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
}
