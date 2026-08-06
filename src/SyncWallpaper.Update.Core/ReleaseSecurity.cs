using System.Reflection;
using System.Text;

namespace SyncWallpaper.Update.Core;

public static class ReleaseUrlValidator
{
    public static bool IsAllowed(Uri? url, GitHubRepositorySettings repository)
    {
        if (url is null || !repository.IsConfigured) return false;
        if (!string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || (url.Port is not -1 and not 443)
            || !string.IsNullOrEmpty(url.UserInfo)) return false;

        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 3
            && string.Equals(Uri.UnescapeDataString(segments[0]), repository.Owner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Uri.UnescapeDataString(segments[1]), repository.Repository, StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[2], "releases", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryValidate(string? value, GitHubRepositorySettings repository, out Uri? url)
    {
        url = null;
        return Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            && IsAllowed(candidate, repository) && (url = candidate) is not null;
    }
}

public static class ReleaseNotesSanitizer
{
    public const int MaxCharacters = 12_000;

    public static string ToPlainText(string? releaseNotes)
    {
        if (string.IsNullOrEmpty(releaseNotes)) return string.Empty;
        var text = releaseNotes.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        // Release notes are deliberately displayed as TextBlock text, never as HTML/Markdown.
        var builder = new StringBuilder(Math.Min(text.Length, MaxCharacters));
        foreach (var ch in text)
        {
            if (char.IsControl(ch) && ch is not '\n' and not '\t') continue;
            builder.Append(ch);
            if (builder.Length >= MaxCharacters) break;
        }
        if (text.Length > builder.Length) builder.Append("\n\n（更新说明过长，已截断；可在 GitHub 查看完整说明。）");
        return builder.ToString();
    }
}

public static class CurrentVersionProvider
{
    public static string GetInformationalVersion(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetEntryAssembly() ?? typeof(CurrentVersionProvider).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational)) return informational.Split('+', 2)[0];
        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    public static SemanticVersion GetSemanticVersion(Assembly? assembly = null)
    {
        var value = GetInformationalVersion(assembly);
        return SemanticVersion.TryParse(value, out var parsed) ? parsed! : SemanticVersion.Parse("0.0.0");
    }
}
