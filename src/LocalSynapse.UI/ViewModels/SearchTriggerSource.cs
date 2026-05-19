namespace LocalSynapse.UI.ViewModels;

/// <summary>
/// Identifies which user action initiated a search invocation. Carried
/// through ExecuteSearchAsync for diagnostic logging and to enable
/// trigger-specific behavior differentiation if needed.
/// </summary>
public enum SearchTriggerSource
{
    /// <summary>Debounce timer fired after user paused typing.</summary>
    Debounce,
    /// <summary>User pressed Enter in the search box.</summary>
    EnterKey,
    /// <summary>User clicked the search button or an example query chip.</summary>
    SearchButton,
    /// <summary>Mode change re-issued the current query.</summary>
    ModeChange,
}
