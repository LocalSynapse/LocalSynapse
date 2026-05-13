namespace LocalSynapse.Core.Models;

/// <summary>Status of the cached license validation state.</summary>
public enum LicenseCacheStatus
{
    /// <summary>License validated successfully within the last 14 days.</summary>
    Valid = 0,
    /// <summary>Network validation failed; retry tomorrow. Pro stays active (fail-open).</summary>
    Stale = 1,
    /// <summary>License revoked by Lemon Squeezy.</summary>
    Revoked = 2,
    /// <summary>License expired.</summary>
    Expired = 3,
    /// <summary>Revoked or expired but within the 14-day grace window.</summary>
    GracePeriod = 4
}
