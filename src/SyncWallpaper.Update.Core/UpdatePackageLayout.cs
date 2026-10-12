using System.Text.Json;

namespace SyncWallpaper.Update.Core;

/// <summary>The only package paths an update may stage or replace. User data never enters this list.</summary>
public static class UpdatePackageLayout
{
    private static readonly HashSet<string> RootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "README.md", "LICENSE", "CHANGELOG.md", "THIRD-PARTY-NOTICES.md", "AppIcon.ico",
        "syncwallpaper-icon.svg", "install.ps1", "upgrade.ps1", "uninstall.ps1", "package-manifest.json"
    };
    private static readonly HashSet<string> ProgramDirectories = new(StringComparer.OrdinalIgnoreCase) { "App", "Diagnostics", "docs" };
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Config", "Wallpapers", "Cache", "Thumbnails", "Logs", "Backups", "Deleted", ".git", "work", "artifacts"
    };

    public static bool IsAllowedRelativePath(string relativePath, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > 240
            || relativePath.Contains('\\') || relativePath.StartsWith('/') || relativePath.Contains(':')) return false;
        var path = isDirectory ? relativePath.TrimEnd('/') : relativePath;
        var parts = path.Split('/');
        if (parts.Any(part => !IsSafeSegment(part) || ProtectedNames.Contains(part))) return false;
        return parts.Length == 1
            ? (isDirectory ? ProgramDirectories.Contains(parts[0]) : RootFiles.Contains(parts[0]))
            : ProgramDirectories.Contains(parts[0]);
    }

    public static void ValidatePackageDirectory(string packageDirectory, string expectedVersion)
    {
        if (!SemanticVersion.TryParse(expectedVersion, out var expected)) throw new InvalidDataException("更新版本号无效。");
        var root = Path.GetFullPath(packageDirectory);
        EnsureNoReparseAncestors(root);
        if (!Directory.Exists(root)) throw new InvalidDataException("更新包目录不存在。");
        ValidateDirectoryEntries(root, root);
        var manifestPath = Path.Combine(root, "package-manifest.json");
        if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 65536)
            throw new InvalidDataException("更新包缺少有效的发布清单。");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var manifest = document.RootElement;
            if (!manifest.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.String
                || !string.Equals(version.GetString(), expected!.ToString(), StringComparison.Ordinal)
                || !manifest.TryGetProperty("rid", out var rid) || rid.GetString() != "win-x64"
                || !manifest.TryGetProperty("selfContained", out var contained) || contained.ValueKind != JsonValueKind.True)
                throw new InvalidDataException("更新包版本或运行平台与所选版本不一致。");
            if (manifest.TryGetProperty("language", out var language)
                && language.GetString() != (expectedVersion.Split('+', 2)[0].EndsWith("-en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh-CN"))
                throw new InvalidDataException("更新包语言与当前版本不一致。");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new InvalidDataException("更新包发布清单无效。", exception);
        }
        var application = Path.Combine(root, "App", "SyncWallpaper.App.exe");
        if (!File.Exists(application) || new FileInfo(application).Length == 0)
            throw new InvalidDataException("更新包缺少主程序。");
    }

    public static void EnsureNoReparseAncestors(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((Directory.Exists(current) || File.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("更新路径不能包含符号链接或目录联接。");
        }
    }

    private static void ValidateDirectoryEntries(string root, string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("更新包不能包含符号链接或目录联接。");
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            if (!IsAllowedRelativePath(Path.GetRelativePath(root, path).Replace('\\', '/'), isDirectory))
                throw new InvalidDataException("更新包包含不允许替换的文件路径。");
            if (isDirectory) ValidateDirectoryEntries(root, path);
        }
    }

    private static bool IsSafeSegment(string value)
    {
        if (value.Length == 0 || value is "." or ".." || value.EndsWith(' ') || value.EndsWith('.')
            || value.Any(c => c < 32 || "<>:\"|?*".Contains(c))) return false;
        var stem = value.Split('.')[0].TrimEnd(' ');
        return !new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³" }
            .Contains(stem, StringComparer.OrdinalIgnoreCase);
    }
}
