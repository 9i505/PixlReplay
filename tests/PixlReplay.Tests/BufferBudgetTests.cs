using PixlReplay.Engine;

namespace PixlReplay.Tests;

public static class BufferBudgetTests
{
    const long GB = 1L << 30;

    [Test]
    public static void Huge_4k_buffer_on_16_gb_is_capped_at_a_quarter_of_ram()
    {
        Assert.Equal(4096, BufferBudget.CapMb(estimateMb: 16000, physicalBytes: 16 * GB), "cap");
    }

    [Test]
    public static void Normal_buffer_on_32_gb_keeps_estimate_plus_headroom()
    {
        Assert.Equal(2746, BufferBudget.CapMb(estimateMb: 2146, physicalBytes: 32 * GB), "cap");
    }

    [Test]
    public static void Tiny_ram_still_allows_512_mb()
    {
        Assert.Equal(512, BufferBudget.CapMb(estimateMb: 2146, physicalBytes: 2 * GB), "cap");
    }

    [Test]
    public static void Unknown_ram_uses_estimate()
    {
        Assert.Equal(2746, BufferBudget.CapMb(estimateMb: 2146, physicalBytes: 0), "cap");
    }
}
