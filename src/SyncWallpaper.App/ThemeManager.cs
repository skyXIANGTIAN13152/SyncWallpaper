using System.Windows;
using System.Windows.Media;
using SyncWallpaper.Core;

namespace SyncWallpaper.App;

public static class ThemeManager
{
    private static ResourceDictionary? _activeResources;
    public static string CurrentThemeId { get; private set; } = AppThemeCatalog.DefaultId;

    public static void Apply(string? themeId)
    {
        var app = System.Windows.Application.Current ?? throw new InvalidOperationException("A WPF application is required.");
        app.Dispatcher.VerifyAccess();
        var theme = AppThemeCatalog.Get(themeId);
        var resources = CreateResources(theme);
        if (_activeResources is null) app.Resources.MergedDictionaries.Add(resources);
        else
        {
            var index = app.Resources.MergedDictionaries.IndexOf(_activeResources);
            if (index >= 0) app.Resources.MergedDictionaries[index] = resources;
            else app.Resources.MergedDictionaries.Add(resources);
        }
        _activeResources = resources;
        CurrentThemeId = theme.Id;
    }

    public static ResourceDictionary CreateResources(AppTheme theme)
    {
        var resources = new ResourceDictionary();
        foreach (var (key, value) in theme.Colors)
        {
            var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));
            brush.Freeze();
            resources[key] = brush;
        }
        resources["ButtonBackground"] = resources["PanelBackground2"];
        resources["AppBackground"] = System.Windows.Media.Brushes.Transparent;
        var header = new LinearGradientBrush(
            ((SolidColorBrush)resources["PanelBackground"]).Color,
            ((SolidColorBrush)resources["PanelBackground2"]).Color, 0);
        header.Freeze();
        resources["HeaderBackground"] = header;
        var accent = new LinearGradientBrush(
            ((SolidColorBrush)resources["Cyan"]).Color,
            ((SolidColorBrush)resources["Purple"]).Color, 0);
        accent.Freeze();
        resources["AccentGradient"] = accent;
        resources["ProfileSpecificAccent"] = resources["Cyan"];
        // The approved Mystery palette uses sage for generic profiles. Other
        // themes retain their contrast-checked secondary accent.
        if (theme.Id == AppThemeCatalog.Mystery.Id)
        {
            var genericAccent = new SolidColorBrush(System.Windows.Media.Color.FromRgb(179, 204, 178));
            genericAccent.Freeze();
            resources["ProfileGenericAccent"] = genericAccent;
        }
        else resources["ProfileGenericAccent"] = resources["Purple"];
        foreach (var (key, source, alpha) in new[] {
            ("TranslucentPanel", "PanelBackground", (byte)224),
            ("TranslucentSidebar", "SidebarBackground", (byte)232),
            ("TranslucentHeader", "PanelBackground", (byte)225) })
        {
            var color = ((SolidColorBrush)resources[source]).Color;
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze(); resources[key] = brush;
        }
        return resources;
    }
}
