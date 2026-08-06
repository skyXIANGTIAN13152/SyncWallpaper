using System.Globalization;
using System.Text.RegularExpressions;

namespace SyncWallpaper.Update.Core;

/// <summary>A small SemVer 2.0 implementation kept dependency-free for the updater UI.</summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private static readonly Regex Pattern = new(
        "^[vV]?(?<major>0|[1-9]\\d*)\\.(?<minor>0|[1-9]\\d*)\\.(?<patch>0|[1-9]\\d*)(?:-(?<pre>[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?(?:\\+(?<build>[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IReadOnlyList<string> _preRelease;

    private SemanticVersion(int major, int minor, int patch, IReadOnlyList<string> preRelease, string buildMetadata)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _preRelease = preRelease;
        BuildMetadata = buildMetadata;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public IReadOnlyList<string> PreReleaseIdentifiers => _preRelease;
    public string BuildMetadata { get; }
    public bool IsPrerelease => _preRelease.Count > 0;

    public static bool TryParse(string? value, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var match = Pattern.Match(value.Trim());
        if (!match.Success) return false;
        if (!int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch)) return false;
        var pre = match.Groups["pre"].Success
            ? match.Groups["pre"].Value.Split('.', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();
        if (pre.Any(x => x.Length > 1 && x[0] == '0' && x.All(char.IsDigit))) return false;
        version = new SemanticVersion(major, minor, patch, pre, match.Groups["build"].Value);
        return true;
    }

    public static SemanticVersion Parse(string value) => TryParse(value, out var version)
        ? version!
        : throw new FormatException($"无效的语义版本：{value}");

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;
        if (!IsPrerelease && !other.IsPrerelease) return 0;
        if (!IsPrerelease) return 1;
        if (!other.IsPrerelease) return -1;
        for (var i = 0; i < Math.Min(_preRelease.Count, other._preRelease.Count); i++)
        {
            var left = _preRelease[i];
            var right = other._preRelease[i];
            if (left == right) continue;
            var leftNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
            if (leftNumeric && rightNumeric) return leftNumber.CompareTo(rightNumber);
            if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;
            return string.CompareOrdinal(left, right);
        }
        return _preRelease.Count.CompareTo(other._preRelease.Count);
    }

    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => Equals(obj as SemanticVersion);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', _preRelease));
    public override string ToString() => $"{Major}.{Minor}.{Patch}"
        + (IsPrerelease ? "-" + string.Join('.', _preRelease) : string.Empty)
        + (!string.IsNullOrWhiteSpace(BuildMetadata) ? "+" + BuildMetadata : string.Empty);

    public Version ToNumericVersion() => new(Major, Minor, Patch);
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
}

public static class ReleaseVersionComparer
{
    public static bool TryParseTag(string? tag, out SemanticVersion? version) => SemanticVersion.TryParse(tag, out version);

    public static bool IsNewer(string? candidate, string? current)
        => SemanticVersion.TryParse(candidate, out var remote)
        && SemanticVersion.TryParse(current, out var local)
        && remote! > local!;

    public static GitHubRelease? SelectHighest(IEnumerable<GitHubRelease> releases, UpdateChannel channel)
        => releases.Where(x => !x.Draft && (channel == UpdateChannel.Beta || !x.Prerelease))
            .Select(x => (Release: x, Parsed: SemanticVersion.TryParse(x.TagName, out var parsed) ? parsed : null))
            .Where(x => x.Parsed is not null)
            .OrderByDescending(x => x.Parsed)
            .Select(x => x.Release)
            .FirstOrDefault();
}
