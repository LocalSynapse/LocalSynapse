namespace LocalSynapse.Core.Interfaces;

public interface ISettingsStore
{
    string GetLanguage();
    void SetLanguage(string cultureName);
    string GetDataFolder();
    string GetLogFolder();
    string GetModelFolder();
    string GetDatabasePath();

    /// <summary>Returns user-configured scan root folders, or null if using defaults.</summary>
    string[]? GetScanRoots();

    /// <summary>Sets scan root folders. Pass null to revert to default behavior.</summary>
    void SetScanRoots(string[]? roots);

    /// <summary>Returns the selected indexing performance mode. Default: "Cruise".</summary>
    string GetPerformanceMode();

    /// <summary>Sets the indexing performance mode. Valid: "Stealth", "Cruise", "Overdrive".</summary>
    void SetPerformanceMode(string mode);
}
