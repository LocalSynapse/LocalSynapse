using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LocalSynapse.UI.Services;

/// <summary>
/// 플랫폼별 동작을 분기하는 정적 헬퍼.
/// </summary>
public static class PlatformHelper
{
    /// <summary>현재 macOS에서 실행 중인지 여부.</summary>
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>현재 Windows에서 실행 중인지 여부.</summary>
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>파일을 기본 프로그램으로 연다.</summary>
    public static void OpenFile(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PlatformHelper] OpenFile failed: {ex.Message}");
        }
    }

    /// <summary>파일 관리자에서 해당 파일을 선택하여 연다.</summary>
    public static void RevealInFileManager(string path)
    {
        try
        {
            if (IsMacOS)
            {
                if (Directory.Exists(path))
                {
                    var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                    psi.ArgumentList.Add(path);
                    Process.Start(psi);
                }
                else if (File.Exists(path))
                {
                    var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                    psi.ArgumentList.Add("-R");
                    psi.ArgumentList.Add(path);
                    Process.Start(psi);
                }
            }
            else
            {
                // Windows — ArgumentList auto-escapes shell metacharacters
                if (Directory.Exists(path))
                {
                    var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                    psi.ArgumentList.Add(path);
                    Process.Start(psi);
                }
                else if (File.Exists(path))
                {
                    var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                    psi.ArgumentList.Add($"/select,{path}");
                    Process.Start(psi);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PlatformHelper] RevealInFileManager failed: {ex.Message}");
        }
    }

    /// <summary>폴더를 파일 관리자로 연다.</summary>
    public static void OpenFolder(string folderPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PlatformHelper] OpenFolder failed: {ex.Message}");
        }
    }

    /// <summary>현재 실행 파일의 이름을 반환한다 (macOS: 확장자 없음, Windows: .exe).</summary>
    public static string GetExecutableName()
    {
        var processPath = Environment.ProcessPath;
        if (processPath != null)
            return processPath;

        var baseName = Path.Combine(AppContext.BaseDirectory, "LocalSynapse");
        return IsWindows ? baseName + ".exe" : baseName;
    }
}
