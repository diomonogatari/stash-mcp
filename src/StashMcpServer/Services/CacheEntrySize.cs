namespace StashMcpServer.Services;

/// <summary>
/// Estimates a cache entry's relative weight for the size-bounded memory cache. Cheaply-measurable
/// large payloads — strings (e.g. file content) and materialized collections — are weighted by their
/// size so they count more toward the cache budget than tiny scalar entries. Opaque objects whose
/// size cannot be measured cheaply default to 1 (the previous, uniform behavior), so this never makes
/// the bound looser than before.
/// </summary>
public static class CacheEntrySize
{
    /// <summary>Upper bound on a single entry's weight, so one huge payload can't dominate the budget.</summary>
    public const int MaxUnits = 256;

    /// <summary>Approximate bytes represented by one weight unit for string payloads.</summary>
    private const int BytesPerUnit = 1024;

    /// <summary>
    /// Estimates the cache weight of a value: ~1 unit per KiB for strings, item count for
    /// collections, and 1 for everything else; clamped to <see cref="MaxUnits"/>.
    /// </summary>
    public static int Estimate(object? value) => value switch
    {
        string s => Math.Clamp(1 + (s.Length / BytesPerUnit), 1, MaxUnits),
        System.Collections.ICollection c => Math.Clamp(c.Count, 1, MaxUnits),
        _ => 1,
    };
}
