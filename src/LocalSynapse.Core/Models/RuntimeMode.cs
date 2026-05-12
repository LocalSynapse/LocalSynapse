namespace LocalSynapse.Core.Models;

/// <summary>Application execution mode, set once at startup.</summary>
public enum RuntimeMode
{
    /// <summary>Full GUI with tray, hotkey, and pipeline.</summary>
    Ui,
    /// <summary>Headless MCP stdio server — no UI, no tray, no hotkey.</summary>
    Mcp,
    /// <summary>Parser quality verification dump mode.</summary>
    Dump
}
