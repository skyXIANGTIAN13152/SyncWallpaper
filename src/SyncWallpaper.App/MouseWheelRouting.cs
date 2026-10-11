using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Control = System.Windows.Controls.Control;
using ComboBox = System.Windows.Controls.ComboBox;

namespace SyncWallpaper.App;

internal static class MouseWheelRouting
{
    // These lists expand inside the page, and closed dropdowns should scroll
    // the editor rather than change a saved monitor or wallpaper selection.
    internal static void ForwardToAncestorScrollViewer(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Control control || control is ComboBox { IsDropDownOpen: true }) return;

        for (var parent = VisualTreeHelper.GetParent(control); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not ScrollViewer scrollViewer) continue;

            e.Handled = true;
            scrollViewer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = Mouse.MouseWheelEvent,
                Source = scrollViewer
            });
            return;
        }
    }
}
