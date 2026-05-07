using LocalSynapse.UI.Services.Localization;
using Xunit;

namespace LocalSynapse.UI.Tests;

public class InstallLocalizationSmokeTest
{
    public static IEnumerable<object[]> InstallKeys() =>
    [
        [StringKeys.Banner.InstallUpdate],
        [StringKeys.Banner.InstallProgress],
        [StringKeys.Banner.InstallVerifying],
        [StringKeys.Banner.InstallLaunching],
        [StringKeys.Banner.InstallRetry],
        [StringKeys.Banner.InstallOpenDownload],
        [StringKeys.Banner.InstallError.Generic],
        [StringKeys.Banner.InstallError.Network],
        [StringKeys.Banner.InstallError.Checksum],
        [StringKeys.Banner.InstallError.Disk],
        [StringKeys.Security.Sends.Receives],  // revised line per SPEC-IU-1 §5.2 / §7
    ];

    [Theory]
    [MemberData(nameof(InstallKeys))]
    public void EveryNewKey_ResolvesInAllFiveLocales(string key)
    {
        var registry = LocalizationRegistry.Build();
        Assert.True(registry.ContainsKey(key), $"Registry missing key: {key}");

        foreach (var locale in new[] { "en", "ko", "fr", "de", "zh" })
        {
            Assert.True(registry[key].TryGetValue(locale, out var value),
                $"Locale '{locale}' missing for key '{key}'");
            Assert.False(string.IsNullOrWhiteSpace(value),
                $"Locale '{locale}' empty for key '{key}'");
        }
    }
}
