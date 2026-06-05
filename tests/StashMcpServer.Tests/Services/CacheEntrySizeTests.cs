using StashMcpServer.Services;

namespace StashMcpServer.Tests.Services;

public class CacheEntrySizeTests
{
    [Fact]
    public void Estimate_Null_IsOne() => Assert.Equal(1, CacheEntrySize.Estimate(null));

    [Fact]
    public void Estimate_Scalar_IsOne() => Assert.Equal(1, CacheEntrySize.Estimate(42));

    [Fact]
    public void Estimate_ShortString_IsOne() => Assert.Equal(1, CacheEntrySize.Estimate("small"));

    [Fact]
    public void Estimate_LargeString_WeightedByLength()
    {
        var weight = CacheEntrySize.Estimate(new string('x', 10 * 1024));
        Assert.True(weight > 1, $"Expected a 10 KiB string to weigh more than 1, got {weight}.");
    }

    [Fact]
    public void Estimate_Collection_WeightedByCount() =>
        Assert.Equal(5, CacheEntrySize.Estimate(new[] { 1, 2, 3, 4, 5 }));

    [Fact]
    public void Estimate_HugePayload_ClampedToMax() =>
        Assert.Equal(CacheEntrySize.MaxUnits, CacheEntrySize.Estimate(new string('x', 10_000 * 1024)));
}