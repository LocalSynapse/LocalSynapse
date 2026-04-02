namespace LocalSynapse.Pipeline.Scanning;

/// <summary>
/// Common filter logic for scan strategies.
/// Ensures consistency between USN and Directory scan approaches.
/// </summary>
public static class ScanFilterHelper
{
    /// <summary>Folders to exclude from scanning.</summary>
    public static readonly HashSet<string> ExcludedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows system
        "Windows", "Program Files", "Program Files (x86)", "ProgramData",
        "$Recycle.Bin", "System Volume Information", "Recovery",
        // User profile special
        "AppData", "WindowsApps", "WpSystem", "MSOCache",
        "Searches", "Links", "3D Objects", "Contacts", "Saved Games",
        "MicrosoftEdgeBackups", "Favorites",
        // Development
        "node_modules", ".git", ".vs", "obj", "bin",
        "__pycache__", ".cache", ".npm", ".nuget",
        // Hardware vendor
        "Intel", "AMD", "NVIDIA",
        // Temp
        "Temp", "tmp",
        // NOTE: OneDrive/Dropbox/Google Drive are NOT excluded.
        // We scan them but skip individual cloud-only files by FileAttributes.
    };

    /// <summary>Image extensions.</summary>
    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp", ".svg"
    };

    /// <summary>Check if folder name is in excluded list.</summary>
    public static bool IsExcludedFolder(string folderName)
        => ExcludedFolders.Contains(folderName);

    /// <summary>Check if extension is an image type.</summary>
    public static bool IsImageExtension(string extension)
        => ImageExtensions.Contains(extension);

    /// <summary>Detect GUID or hex-hash folder names (app cache, sync metadata).</summary>
    public static bool IsGuidOrHashFolder(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return false;

        if (folderName.Length == 36
            && folderName[8] == '-'
            && folderName[13] == '-'
            && folderName[18] == '-'
            && folderName[23] == '-')
            return true;

        if (folderName.Length >= 16 && !folderName.Contains('.'))
        {
            foreach (var c in folderName)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F') || c == '-'))
                    return false;
            }
            return true;
        }

        return false;
    }

    /// <summary>Check if directory has hidden or system attributes.</summary>
    public static bool IsHiddenOrSystem(FileAttributes attributes)
        => (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    /// <summary>
    /// Check if file is a cloud placeholder (OneDrive/iCloud/Dropbox cloud-only).
    /// Uses FileAttributes from FileSystemEntry — does NOT open the file,
    /// so cloud download is physically impossible.
    /// </summary>
    public static bool IsCloudPlaceholder(FileAttributes attrs)
    {
        const FileAttributes offline = FileAttributes.Offline;
        const FileAttributes recallOnAccess = (FileAttributes)0x00400000;
        const FileAttributes recallOnOpen = (FileAttributes)0x00200000;
        return attrs.HasFlag(offline) || attrs.HasFlag(recallOnAccess) || attrs.HasFlag(recallOnOpen);
    }

    /// <summary>
    /// Check if file should be completely skipped (not even added to DB).
    /// System files, reparse points (junctions), tiny files, huge files.
    /// </summary>
    public static bool ShouldSkipFile(FileAttributes attrs, long fileSize)
    {
        if (attrs.HasFlag(FileAttributes.System)) return true;
        if (attrs.HasFlag(FileAttributes.ReparsePoint)) return true;
        if (fileSize < 10) return true;              // broken/empty
        if (fileSize > 500_000_000) return true;     // >500MB dumps/logs
        return false;
    }

    /// <summary>Detect if path is inside a cloud sync folder.</summary>
    public static bool IsCloudSyncPath(string filePath)
    {
        return filePath.Contains("OneDrive", StringComparison.OrdinalIgnoreCase)
            || filePath.Contains("Dropbox", StringComparison.OrdinalIgnoreCase)
            || filePath.Contains("Google Drive", StringComparison.OrdinalIgnoreCase)
            || filePath.Contains("iCloudDrive", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Comprehensive directory skip check.</summary>
    public static bool ShouldSkipDirectory(string name, FileAttributes attributes)
    {
        if (name.StartsWith('.')) return true;
        if (IsExcludedFolder(name)) return true;
        if (IsGuidOrHashFolder(name)) return true;
        if (IsHiddenOrSystem(attributes)) return true;
        return false;
    }
}
