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

    /// <summary>Sets the indexing performance mode. Valid: "Stealth", "Cruise", "Overdrive", "MadMax".</summary>
    void SetPerformanceMode(string mode);

    /// <summary>Returns cached GPU detection result.</summary>
    (string? bestProvider, string? gpuName) GetGpuDetectionCache();

    /// <summary>Caches GPU detection result.</summary>
    void SetGpuDetectionCache(string? bestProvider, string? gpuName);

    /// <summary>Returns the last known EP runtime status. (null, null, null) if never set.</summary>
    (string? status, string? activeEp, string? detail) GetEpRuntimeStatus();

    /// <summary>Records EP runtime status. No-op (no disk write) if all three values match the current state.</summary>
    void SetEpRuntimeStatus(string? status, string? activeEp, string? detail);

    // ── Always-On settings (v2.13.0) ──
    // ⚠️ CLAUDE.md Interfaces/ 수정 예외 승인 (2026-05-11): additive-only, 기존 메서드 변경 없음.

    /// <summary>Returns the global hotkey combo string, or null for platform default.</summary>
    string? GetHotkeyCombo();
    /// <summary>Sets the global hotkey combo string.</summary>
    void SetHotkeyCombo(string? combo);

    /// <summary>Returns whether the global hotkey is enabled. Default: true.</summary>
    bool GetHotkeyEnabled();
    /// <summary>Sets hotkey enabled state.</summary>
    void SetHotkeyEnabled(bool enabled);

    /// <summary>Returns whether X-button minimizes to tray. Default: true.</summary>
    bool GetMinimizeToTrayOnClose();
    /// <summary>Sets minimize-to-tray behavior.</summary>
    void SetMinimizeToTrayOnClose(bool enabled);

    /// <summary>Returns whether the first-close toast should be shown. Default: true.</summary>
    bool GetShowFirstCloseToast();
    /// <summary>Sets the first-close toast flag (flipped to false after shown).</summary>
    void SetShowFirstCloseToast(bool show);

    /// <summary>Returns whether auto-start is enabled. Null means not yet set by onboarding.</summary>
    bool? GetAutoStartEnabled();
    /// <summary>Sets auto-start state.</summary>
    void SetAutoStartEnabled(bool enabled);

    /// <summary>Returns the last seen version string, or null if pre-2.13.0.</summary>
    string? GetLastSeenVersion();
    /// <summary>Sets the last seen version after onboarding dialog is dismissed.</summary>
    void SetLastSeenVersion(string version);
}
