using System.Collections.Generic;
using System.Reflection;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.UI.Services.Localization;
using Xunit;

namespace LocalSynapse.UI.Tests;

/// <summary>In-memory ISettingsStore fake for unit tests.</summary>
internal sealed class FakeSettingsStore : ISettingsStore
{
    public string Language { get; set; } = "en";

    public string GetLanguage() => Language;
    public void SetLanguage(string cultureName) => Language = cultureName;
    public string GetDataFolder() => "/tmp/ls-test";
    public string GetLogFolder() => "/tmp/ls-test/logs";
    public string GetModelFolder() => "/tmp/ls-test/models";
    public string GetDatabasePath() => "/tmp/ls-test/ls.db";
    public string[]? GetScanRoots() => null;
    public void SetScanRoots(string[]? roots) { }
    public string GetPerformanceMode() => "Cruise";
    public void SetPerformanceMode(string mode) { }
}

public class LocalizationServiceTests
{
    [Fact]
    public void AllStringKeysHaveRegistryEntries()
    {
        var registry = LocalizationRegistry.Build();
        var missing = new List<string>();

        foreach (var keyValue in CollectAllStringKeys())
        {
            if (!registry.ContainsKey(keyValue))
                missing.Add(keyValue);
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void AllRegistryEntriesHaveNonEmptyEnAndKo()
    {
        var registry = LocalizationRegistry.Build();
        var incomplete = new List<string>();

        foreach (var kvp in registry)
        {
            var locales = kvp.Value;
            if (!locales.TryGetValue("en", out var en) || string.IsNullOrEmpty(en)
                || !locales.TryGetValue("ko", out var ko) || string.IsNullOrEmpty(ko))
                incomplete.Add(kvp.Key);
        }

        Assert.Empty(incomplete);
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("ko", "ko")]
    [InlineData("ko-KR", "ko")]
    [InlineData("ko-KP", "ko")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void NormalizesLocaleCodes(string? input, string expected)
    {
        var store = new FakeSettingsStore { Language = input ?? "" };
        var svc = new LocalizationService(store);
        Assert.Equal(expected, svc.Current);
    }

    [Fact]
    public void SetLanguageFiresLanguageChanged()
    {
        var store = new FakeSettingsStore { Language = "en" };
        var svc = new LocalizationService(store);

        var fired = 0;
        svc.LanguageChanged += (_, _) => fired++;

        svc.SetLanguage("ko");
        Assert.Equal(1, fired);
        Assert.Equal("ko", svc.Current);
        Assert.Equal("ko", store.Language);
    }

    [Fact]
    public void SetLanguageToSameValueDoesNotFire()
    {
        var store = new FakeSettingsStore { Language = "en" };
        var svc = new LocalizationService(store);

        var fired = 0;
        svc.LanguageChanged += (_, _) => fired++;

        svc.SetLanguage("en");
        svc.SetLanguage("en-US"); // normalizes to "en" — same as current
        Assert.Equal(0, fired);
    }

    [Fact]
    public void ConstructorNormalizesStoredKoKrToKo()
    {
        var store = new FakeSettingsStore { Language = "ko-KR" };
        var svc = new LocalizationService(store);
        Assert.Equal("ko", svc.Current);
        Assert.Equal("ko", store.Language); // persisted normalized
    }

    [Fact]
    public void IndexerReturnsLocalizedString()
    {
        var store = new FakeSettingsStore { Language = "en" };
        var svc = new LocalizationService(store);
        var en = svc[StringKeys.Nav.Search];
        svc.SetLanguage("ko");
        var ko = svc[StringKeys.Nav.Search];
        Assert.Equal("Search", en);
        Assert.Equal("검색", ko);
    }

    [Fact]
    public void FormatAppliesArgs()
    {
        var store = new FakeSettingsStore { Language = "en" };
        var svc = new LocalizationService(store);
        var text = svc.Format(StringKeys.Folder.FileCount, 42);
        Assert.Equal("42 files", text);
    }

    /// <summary>
    /// Collects all string constant values from StringKeys and nested types via reflection.
    /// </summary>
    private static IEnumerable<string> CollectAllStringKeys()
    {
        foreach (var value in CollectConstantsRecursive(typeof(StringKeys)))
            yield return value;
    }

    private static IEnumerable<string> CollectConstantsRecursive(System.Type t)
    {
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (f.IsLiteral && f.FieldType == typeof(string))
            {
                var v = f.GetRawConstantValue() as string;
                if (v != null) yield return v;
            }
        }
        foreach (var nested in t.GetNestedTypes(BindingFlags.Public | BindingFlags.Static))
        {
            foreach (var v in CollectConstantsRecursive(nested))
                yield return v;
        }
    }
}
