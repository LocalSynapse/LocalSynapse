namespace LocalSynapse.Core.Models;

/// <summary>
/// Payload persisted in the encrypted license cache file (license.enc).
/// All fields are required for a valid cache entry.
/// </summary>
public sealed class LicenseCacheData
{
    /// <summary>The Lemon Squeezy license key string.</summary>
    public required string LicenseKey { get; init; }

    /// <summary>Instance ID assigned by Lemon Squeezy on activation.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Lemon Squeezy product ID.</summary>
    public required string ProductId { get; init; }

    /// <summary>License tier derived from product.</summary>
    public required LicenseTier Tier { get; init; }

    /// <summary>Timestamp when the license was first activated on this device.</summary>
    public required DateTimeOffset ActivatedAt { get; init; }

    /// <summary>License expiration date (update entitlement end).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Timestamp of the most recent successful validation.</summary>
    public required DateTimeOffset LastValidatedAt { get; set; }

    /// <summary>Current validation status.</summary>
    public required LicenseCacheStatus Status { get; set; }
}
