namespace SyncWallpaper.Core;

/// <summary>Opaque semantic colors keep controls readable regardless of the desktop wallpaper.</summary>
public sealed record AppTheme(string Id, string Name, string Description, IReadOnlyDictionary<string, string> Colors)
{
    public string PreviewSurface => Colors["WindowSurface"];
    public string PreviewCard => Colors["GlassCardBackground"];
    public string PreviewAccent => Colors["Cyan"];
    public string PreviewSecondary => Colors["Purple"];
}

public static class AppThemeCatalog
{
    public const string DefaultId = "Aurora";

    public static AppTheme Aurora { get; } = Create("Aurora", "极光科技", "深空蓝 · 冰青 · 星云紫",
        "#0D1322", "#10192B", "#141F34", "#1B2340", "#18253D", "#0E182B", "#17243A",
        "#EAF1FD", "#B0BED5", "#90A0B9", "#536A89", "#30435F", "#263C55", "#314C68",
        "#28465B", "#F0FAFF", "#67DDF0", "#94B8FF", "#C6ABFF", "#072031", "#9CEAF6",
        "#62DBAA", "#FF9BA6", "#F0CD84");

    public static AppTheme Graphite { get; } = Create("Graphite", "石墨暗色", "中性石墨 · 柔和薄荷",
        "#15181E", "#1A1E26", "#202630", "#222E32", "#222832", "#171D26", "#292F3A",
        "#F3F4F6", "#BFC6D3", "#A2ACBC", "#657286", "#3B4554", "#343E4C", "#3F4C5F",
        "#344D48", "#F2FFF9", "#87DFC5", "#ACC8F0", "#D0B8ED", "#102820", "#AFF0DC",
        "#86E2AB", "#FFA4AA", "#EED099");

    public static AppTheme Daylight { get; } = Create("Daylight", "云白亮色", "雾白底色 · 钴蓝强调",
        "#F3F6FC", "#EAF0F8", "#FFFFFF", "#E9EFF9", "#FFFFFF", "#F8FAFE", "#FFFFFF",
        "#17253D", "#485B77", "#5D6A7E", "#8292AB", "#D1DBEA", "#E1EAF8", "#CCDDF5",
        "#D5E4FF", "#13346C", "#1252B8", "#2D57B3", "#7343AD", "#FFFFFF", "#0F4294",
        "#177348", "#B72F46", "#865706");

    public static IReadOnlyList<AppTheme> All { get; } = Array.AsReadOnly(new[] { Aurora, Graphite, Daylight });

    public static AppTheme Get(string? id) => All.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? Aurora;

    private static AppTheme Create(string id, string name, string description, params string[] values)
    {
        string[] keys = ["WindowSurface", "SidebarBackground", "PanelBackground", "PanelBackground2",
            "GlassCardBackground", "InputBackground", "PopupBackground", "TextPrimary", "TextSecondary",
            "TextDisabled", "Border", "GlassBorder", "ControlHover", "ControlPressed", "SelectionBackground",
            "SelectionText", "Cyan", "Blue", "Purple", "OnAccent", "AccentHover", "Success", "Danger", "Warning"];
        if (keys.Length != values.Length) throw new ArgumentException("Every theme must supply the complete semantic palette.");
        return new AppTheme(id, name, description,
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(keys.Zip(values).ToDictionary(x => x.First, x => x.Second)));
    }
}
