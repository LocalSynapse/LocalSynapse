using System.Diagnostics;
using System.Text;
using FluentAssertions;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;
using LocalSynapse.Core.Repositories;
using Xunit;

namespace LocalSynapse.Core.Tests;

public sealed class LicenseCacheStoreTest : IDisposable
{
    private readonly string _tempDir;
    private readonly ILicenseCacheStore _store;

    public LicenseCacheStoreTest()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ls-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var settings = new SettingsStore(_tempDir);
        _store = new LicenseCacheStore(settings);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (Exception ex) { Debug.WriteLine($"[LicenseCacheStoreTest] Cleanup failed: {ex.Message}"); }
    }

    private static LicenseCacheData CreateSample(
        LicenseTier tier = LicenseTier.Pro,
        LicenseCacheStatus status = LicenseCacheStatus.Valid,
        string licenseKey = "LSP3-A4FK-92ZQ-XXXX-7BJW")
    {
        var now = DateTimeOffset.UtcNow;
        return new LicenseCacheData
        {
            LicenseKey = licenseKey,
            InstanceId = "inst_test_001",
            ProductId = "prod_timeline_001",
            Tier = tier,
            ActivatedAt = now,
            ExpiresAt = now.AddYears(1),
            LastValidatedAt = now,
            Status = status
        };
    }

    private string LicenseFilePath => Path.Combine(_tempDir, "license.enc");

    // ── 3.1 Round-trip ──

    [Fact]
    public void Save_Then_Load_Returns_Identical_Data()
    {
        var original = CreateSample();
        _store.Save(original);

        var loaded = _store.Load();

        loaded.Should().NotBeNull();
        loaded!.LicenseKey.Should().Be(original.LicenseKey);
        loaded.InstanceId.Should().Be(original.InstanceId);
        loaded.ProductId.Should().Be(original.ProductId);
        loaded.Tier.Should().Be(original.Tier);
        loaded.ActivatedAt.Should().Be(original.ActivatedAt);
        loaded.ExpiresAt.Should().Be(original.ExpiresAt);
        loaded.LastValidatedAt.Should().Be(original.LastValidatedAt);
        loaded.Status.Should().Be(original.Status);
    }

    // ── 3.2 No file ──

    [Fact]
    public void Load_Returns_Null_When_No_File_Exists()
    {
        _store.Load().Should().BeNull();
    }

    // ── 3.3 Corrupted file ──

    [Fact]
    public void Load_Returns_Null_When_File_Is_Corrupted()
    {
        _store.Save(CreateSample());
        var bytes = File.ReadAllBytes(LicenseFilePath);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(LicenseFilePath, bytes);

        _store.Load().Should().BeNull();
    }

    // ── 3.4 Truncated file ──

    [Fact]
    public void Load_Returns_Null_When_File_Is_Truncated()
    {
        _store.Save(CreateSample());
        File.WriteAllBytes(LicenseFilePath, new byte[5]);

        _store.Load().Should().BeNull();
    }

    // ── 3.5 Empty file ──

    [Fact]
    public void Load_Returns_Null_When_File_Is_Empty()
    {
        File.WriteAllBytes(LicenseFilePath, Array.Empty<byte>());

        _store.Load().Should().BeNull();
    }

    // ── 3.6 Clear deletes ──

    [Fact]
    public void Clear_Deletes_The_File()
    {
        _store.Save(CreateSample());
        File.Exists(LicenseFilePath).Should().BeTrue();

        _store.Clear();

        File.Exists(LicenseFilePath).Should().BeFalse();
        _store.Load().Should().BeNull();
    }

    // ── 3.7 Clear no-op ──

    [Fact]
    public void Clear_Is_NoOp_When_No_File_Exists()
    {
        var act = () => _store.Clear();
        act.Should().NotThrow();
    }

    // ── 3.8 Overwrite ──

    [Fact]
    public void Save_Overwrites_Previous_Cache()
    {
        _store.Save(CreateSample(tier: LicenseTier.Free));
        _store.Save(CreateSample(tier: LicenseTier.Pro));

        var loaded = _store.Load();
        loaded.Should().NotBeNull();
        loaded!.Tier.Should().Be(LicenseTier.Pro);
    }

    // ── 3.9 File created ──

    [Fact]
    public void Save_Creates_License_Enc_In_Data_Folder()
    {
        File.Exists(LicenseFilePath).Should().BeFalse();

        _store.Save(CreateSample());

        File.Exists(LicenseFilePath).Should().BeTrue();
    }

    // ── 3.10 Not plain text ──

    [Fact]
    public void Saved_File_Is_Not_Plain_Text_Readable()
    {
        var key = "TEST-KEY-1234";
        _store.Save(CreateSample(licenseKey: key));

        var rawText = Encoding.UTF8.GetString(File.ReadAllBytes(LicenseFilePath));
        rawText.Should().NotContain(key);
    }

    // ── 3.11 Status default ──

    [Fact]
    public void LicenseCacheStatus_Default_Is_Valid()
    {
        default(LicenseCacheStatus).Should().Be(LicenseCacheStatus.Valid);
    }

    // ── 3.12 Status member count ──

    [Fact]
    public void LicenseCacheStatus_Has_Exactly_Five_Values()
    {
        var values = Enum.GetValues<LicenseCacheStatus>();
        values.Should().HaveCount(5);
        values.Should().Contain(LicenseCacheStatus.Valid);
        values.Should().Contain(LicenseCacheStatus.Stale);
        values.Should().Contain(LicenseCacheStatus.Revoked);
        values.Should().Contain(LicenseCacheStatus.Expired);
        values.Should().Contain(LicenseCacheStatus.GracePeriod);
    }
}
