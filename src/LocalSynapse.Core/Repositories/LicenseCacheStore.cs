using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;

namespace LocalSynapse.Core.Repositories;

/// <summary>
/// Reads, writes, and clears the encrypted license cache file (license.enc).
/// Encryption: AES-256-GCM with a PBKDF2-derived key from machine identity.
/// </summary>
public sealed class LicenseCacheStore : ILicenseCacheStore
{
    private const string FileName = "license.enc";
    private const int NonceSize = 12;   // AES-GCM standard
    private const int TagSize = 16;     // AES-GCM standard
    private const int KeySize = 32;     // AES-256
    private const int Pbkdf2Iterations = 100_000;

    // App-specific salt — prevents rainbow tables; not secret.
    private static readonly byte[] AppSalt =
    {
        0x4C, 0x6F, 0x63, 0x61, 0x6C, 0x53, 0x79, 0x6E,
        0x61, 0x70, 0x73, 0x65, 0x2D, 0x76, 0x33, 0x2D,
        0x4C, 0x69, 0x63, 0x65, 0x6E, 0x73, 0x65, 0x2D,
        0x43, 0x61, 0x63, 0x68, 0x65, 0x2D, 0x53, 0x61
    };
    // ASCII: "LocalSynapse-v3-License-Cache-Sa"

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly string _filePath;

    /// <summary>Creates a license cache store using the data folder from settings.</summary>
    public LicenseCacheStore(ISettingsStore settings)
    {
        _filePath = Path.Combine(settings.GetDataFolder(), FileName);
    }

    /// <inheritdoc />
    public LicenseCacheData? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return null;

            var fileBytes = File.ReadAllBytes(_filePath);
            if (fileBytes.Length < NonceSize + TagSize + 1)
                return null;

            var nonce = fileBytes.AsSpan(0, NonceSize);
            var ciphertextLength = fileBytes.Length - NonceSize - TagSize;
            var ciphertext = fileBytes.AsSpan(NonceSize, ciphertextLength);
            var tag = fileBytes.AsSpan(NonceSize + ciphertextLength, TagSize);

            var key = DeriveKey();
            try
            {
                var plaintext = new byte[ciphertextLength];
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);

                return JsonSerializer.Deserialize<LicenseCacheData>(
                    Encoding.UTF8.GetString(plaintext), JsonOptions);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LicenseCacheStore] Load failed: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc />
    public void Save(LicenseCacheData data)
    {
        try
        {
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[jsonBytes.Length];
            var tag = new byte[TagSize];

            var key = DeriveKey();
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Encrypt(nonce, jsonBytes, ciphertext, tag);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            // File format: [nonce 12B][ciphertext NB][tag 16B]
            var fileBytes = new byte[NonceSize + ciphertext.Length + TagSize];
            nonce.CopyTo(fileBytes, 0);
            ciphertext.CopyTo(fileBytes, NonceSize);
            tag.CopyTo(fileBytes, NonceSize + ciphertext.Length);

            WriteAtomic(fileBytes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LicenseCacheStore] Save failed: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LicenseCacheStore] Clear failed: {ex.Message}");
        }
    }

    private void WriteAtomic(byte[] data)
    {
        var tempPath = _filePath + ".tmp";
        var backupPath = _filePath + ".bak";

        File.WriteAllBytes(tempPath, data);

        if (File.Exists(_filePath))
        {
            File.Replace(tempPath, _filePath, backupPath);
            try { File.Delete(backupPath); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LicenseCacheStore] Backup cleanup failed: {ex.Message}");
            }
        }
        else
        {
            File.Move(tempPath, _filePath);
        }
    }

    private static byte[] DeriveKey()
    {
        var identity = Encoding.UTF8.GetBytes(
            Environment.MachineName + "\x1f" + Environment.UserName);
        return Rfc2898DeriveBytes.Pbkdf2(
            identity, AppSalt, Pbkdf2Iterations,
            HashAlgorithmName.SHA256, KeySize);
    }
}
