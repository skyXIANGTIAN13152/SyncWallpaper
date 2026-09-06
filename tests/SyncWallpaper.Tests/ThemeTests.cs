using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SyncWallpaper.App;
using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public class ThemeTests
{
    [TestMethod]
    public void EveryThemeHasTheSameCompleteOpaquePalette()
    {
        foreach (var theme in AppThemeCatalog.All)
        {
            CollectionAssert.AreEquivalent(AppThemeCatalog.Aurora.Colors.Keys.ToArray(), theme.Colors.Keys.ToArray());
            Assert.IsTrue(theme.Colors.Values.All(value => value.Length == 7 && value[0] == '#'));
        }
        Assert.AreEqual(3, AppThemeCatalog.All.Select(x => x.Id).Distinct().Count());
    }

    [TestMethod]
    public void NormalSelectedAndDisabledTextHaveAtLeastFourPointFiveContrast()
    {
        string[] surfaces = ["WindowSurface", "SidebarBackground", "PanelBackground", "PanelBackground2",
            "GlassCardBackground", "InputBackground", "PopupBackground"];
        foreach (var theme in AppThemeCatalog.All)
        {
            foreach (var foreground in new[] { "TextPrimary", "TextSecondary", "TextDisabled", "Cyan", "Blue", "Purple", "Success", "Danger" })
                foreach (var surface in surfaces)
                    AssertContrast(theme, foreground, surface);
            AssertContrast(theme, "TextPrimary", "ControlHover");
            AssertContrast(theme, "TextPrimary", "ControlPressed");
            AssertContrast(theme, "TextSecondary", "ControlHover");
            AssertContrast(theme, "SelectionText", "SelectionBackground");
            AssertContrast(theme, "OnAccent", "Cyan");
            AssertContrast(theme, "OnAccent", "AccentHover");
        }
    }

    [TestMethod]
    public void ThemePreferenceRoundTripsWithoutChangingWallpaperOrStartupSettings()
    {
        foreach (var theme in AppThemeCatalog.All)
        {
            var settings = new AppSettings { UiTheme = theme.Id, AutoMatchEnabled = true, StartWithWindows = true,
                EditingProfileId = "editing", LastMatchedProfileId = "matched" };
            var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
            Assert.IsFalse(AppSettingsMigrator.Migrate(loaded));
            Assert.AreEqual(theme.Id, loaded.UiTheme);
            Assert.IsTrue(loaded.AutoMatchEnabled && loaded.StartWithWindows);
            Assert.AreEqual("editing", loaded.EditingProfileId);
            Assert.AreEqual("matched", loaded.LastMatchedProfileId);
        }
    }

    [TestMethod]
    public void MissingOrUnknownThemeSafelyUsesAurora()
    {
        foreach (var value in new string?[] { null, "", "deleted-theme", "aurora" })
        {
            var settings = new AppSettings { SchemaVersion = 3, UiTheme = value! };
            Assert.IsTrue(AppSettingsMigrator.Migrate(settings));
            Assert.AreEqual(AppThemeCatalog.DefaultId, settings.UiTheme);
            Assert.IsFalse(AppSettingsMigrator.Migrate(settings));
        }
    }

    [TestMethod]
    public void ControlTemplatesKeepInheritedTextReadableAcrossLivePaletteChanges()
    {
        RunSta(() =>
        {
            var styles = (ResourceDictionary)System.Windows.Application.LoadComponent(
                new Uri("/SyncWallpaper.App;component/Themes/Controls.xaml", UriKind.Relative));
            var root = new StackPanel { Width = 700 };
            root.Resources.MergedDictionaries.Add(styles);
            root.Resources.MergedDictionaries.Add(ThemeManager.CreateResources(AppThemeCatalog.Aurora));
            var accentText = new TextBlock { Text = "保存修改" };
            var primary = new Button { Content = accentText, Style = (Style)styles["PrimaryButton"] };
            var combo = new ComboBox { ItemsSource = new[] { "Stable", "Beta" }, SelectedIndex = 0, Width = 220 };
            var itemText = new TextBlock { Text = "竖屏壁纸" };
            var item = new ComboBoxItem { Content = itemText, IsSelected = true };
            var disabled = new Button { Content = "暂不可用", IsEnabled = false };
            var input = new TextBox { Text = "组合名称" };
            root.Children.Add(primary);
            root.Children.Add(combo);
            root.Children.Add(item);
            root.Children.Add(disabled);
            root.Children.Add(input);
            foreach (var theme in AppThemeCatalog.All)
            {
                root.Resources.MergedDictionaries[1] = ThemeManager.CreateResources(theme);
                root.Measure(new Size(700, 600)); root.Arrange(new Rect(0, 0, 700, 600)); root.UpdateLayout();
                foreach (var control in root.Children.OfType<Control>()) control.ApplyTemplate();
                root.UpdateLayout();
                AssertBrush(theme, "OnAccent", accentText.Foreground);
                AssertBrush(theme, "TextPrimary", combo.Foreground);
                AssertBrush(theme, "InputBackground", combo.Background);
                AssertBrush(theme, "SelectionText", itemText.Foreground);
                AssertBrush(theme, "SelectionBackground", item.Background);
                AssertBrush(theme, "TextDisabled", disabled.Foreground);
                AssertBrush(theme, "InputBackground", input.Background);
                Assert.IsNotNull(combo.Template.FindName("PART_Popup", combo));
                Assert.IsNotNull(input.Template.FindName("PART_ContentHost", input));
                Assert.IsTrue(primary.IsEnabled);
                Assert.AreEqual("Stable", combo.SelectedItem);
                Assert.AreEqual("组合名称", input.Text);
            }
        });
    }

    private static void AssertBrush(AppTheme theme, string key, Brush actual)
        => Assert.AreEqual((Color)ColorConverter.ConvertFromString(theme.Colors[key]), ((SolidColorBrush)actual).Color, $"{theme.Id}/{key}");

    [DataTestMethod]
    [DataRow(39d, 1d)]
    [DataRow(39d, 1.25d)]
    [DataRow(39d, 1.5d)]
    [DataRow(38d, 2d)]
    [DataRow(36d, 1d)]
    public void CompactTextBoxesLeaveRoomForTheEntireChineseTextLine(double height, double scale)
    {
        RunSta(() =>
        {
            var root = new StackPanel { Width = 400, LayoutTransform = new ScaleTransform(scale, scale), UseLayoutRounding = true };
            root.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Application.LoadComponent(
                new Uri("/SyncWallpaper.App;component/Themes/Controls.xaml", UriKind.Relative)));
            root.Resources.MergedDictionaries.Add(ThemeManager.CreateResources(AppThemeCatalog.Aurora));
            var input = new TextBox { Text = "手动确认配置 Ag屏序", Height = height, FontSize = 14, Width = 250 };
            root.Children.Add(input);
            root.Measure(new Size(800, 500)); root.Arrange(new Rect(0, 0, 800, 500)); root.UpdateLayout();
            var host = (ScrollViewer)input.Template.FindName("PART_ContentHost", input);
            var glyph = input.GetRectFromCharacterIndex(0);
            Assert.IsTrue(glyph.Height > 0);
            Assert.IsTrue(host.ViewportHeight >= glyph.Height,
                $"Height {height}, scale {scale}: viewport {host.ViewportHeight} clips a {glyph.Height}-pixel line.");
        });
    }

    private static void AssertContrast(AppTheme theme, string text, string surface)
    {
        var a = Luminance(theme.Colors[text]); var b = Luminance(theme.Colors[surface]);
        var ratio = (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
        Assert.IsTrue(ratio >= 4.5, $"{theme.Id}: {text} on {surface} is only {ratio:F2}:1");
    }

    private static double Luminance(string hex)
    {
        var channels = Enumerable.Range(0, 3).Select(i => int.Parse(hex.Substring(1 + i * 2, 2), NumberStyles.HexNumber) / 255d)
            .Select(value => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4)).ToArray();
        return .2126 * channels[0] + .7152 * channels[1] + .0722 * channels[2];
    }

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "WPF style test did not complete.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
