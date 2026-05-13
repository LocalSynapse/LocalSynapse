using LocalSynapse.Core.Models;

namespace LocalSynapse.Core.Interfaces;

/// <summary>
/// Read/write/clear for the encrypted license cache file (license.enc).
/// All methods are synchronous — the file is small (&lt;1 KB).
/// </summary>
public interface ILicenseCacheStore
{
    /// <summary>
    /// Reads and decrypts the license cache.
    /// Returns null if the file is absent, corrupted, or cannot be decrypted.
    /// </summary>
    LicenseCacheData? Load();

    /// <summary>Encrypts and writes the license cache atomically.</summary>
    void Save(LicenseCacheData data);

    /// <summary>Deletes the license cache file. No-op if absent.</summary>
    void Clear();
}
