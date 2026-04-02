namespace LocalSynapse.Core.Interfaces;

public interface ISettingsStore
{
    string GetLanguage();
    void SetLanguage(string cultureName);
    string GetDataFolder();
    string GetLogFolder();
    string GetModelFolder();
    string GetDatabasePath();
}
