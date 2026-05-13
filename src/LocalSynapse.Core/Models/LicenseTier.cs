namespace LocalSynapse.Core.Models;

/// <summary>
/// User's current license tier. Determines which features are available.
/// Orthogonal to <see cref="RuntimeMode"/> (execution mode).
/// </summary>
public enum LicenseTier
{
    /// <summary>Default tier. 30-day rolling window, Tier 1 only, no cloud sync.</summary>
    Free = 0,
    /// <summary>Licensed tier. Full archive, Tier 2 sync, up to 3 devices.</summary>
    Pro = 1
}
