using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LocalSynapse.Core.Interfaces;
using LocalSynapse.Core.Models;

namespace LocalSynapse.UI.Services;

/// <summary>
/// Manages auto-start registration on login.
/// Windows: HKCU Run registry key. macOS: LaunchAgent plist.
/// </summary>
public sealed class AutoStartService
{
    private readonly RuntimeMode _mode;
    private readonly ISettingsStore _settings;
    private const string AppName = "LocalSynapse";

    /// <summary>Creates a new AutoStartService.</summary>
    public AutoStartService(RuntimeMode mode, ISettingsStore settings)
    {
        _mode = mode;
        _settings = settings;
    }

    /// <summary>Enable auto-start on login.</summary>
    public void Enable()
    {
        if (_mode != RuntimeMode.Ui) return;
        _settings.SetAutoStartEnabled(true);

        var exePath = PlatformHelper.GetExecutableName();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            EnableWindows(exePath);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            EnableMacOs(exePath);
        }
    }

    /// <summary>Disable auto-start on login.</summary>
    public void Disable()
    {
        if (_mode != RuntimeMode.Ui) return;
        _settings.SetAutoStartEnabled(false);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            DisableWindows();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            DisableMacOs();
        }
    }

    [SupportedOSPlatform("windows")]
    private static void EnableWindows(string exePath)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.SetValue(AppName, $"\"{exePath}\" --minimized");
            Debug.WriteLine($"[AutoStart] Windows registry set: {exePath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoStart] Windows enable failed: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void DisableWindows()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
            Debug.WriteLine("[AutoStart] Windows registry cleared");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoStart] Windows disable failed: {ex.Message}");
        }
    }

    private static void EnableMacOs(string exePath)
    {
        try
        {
            var launchAgentsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "LaunchAgents");
            Directory.CreateDirectory(launchAgentsDir);
            var plistPath = Path.Combine(launchAgentsDir, "com.localsynapse.app.plist");

            var plistContent = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>com.localsynapse.app</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>{exePath}</string>
                        <string>--minimized</string>
                    </array>
                    <key>RunAtLoad</key>
                    <true/>
                    <key>KeepAlive</key>
                    <false/>
                </dict>
                </plist>
                """;

            File.WriteAllText(plistPath, plistContent);
            Debug.WriteLine($"[AutoStart] macOS LaunchAgent written: {plistPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoStart] macOS enable failed: {ex.Message}");
        }
    }

    private static void DisableMacOs()
    {
        try
        {
            var plistPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "LaunchAgents", "com.localsynapse.app.plist");
            if (File.Exists(plistPath))
            {
                File.Delete(plistPath);
                Debug.WriteLine("[AutoStart] macOS LaunchAgent removed");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoStart] macOS disable failed: {ex.Message}");
        }
    }
}
