using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SyncWallpaper.App;

namespace SyncWallpaper.Tests;

[TestClass]
public class MouseWheelRoutingTests
{
    [DataTestMethod]
    [DataRow(-120)]
    [DataRow(120)]
    public void ExpandedListScrollsThePageInBothDirections(int delta)
    {
        RunSta(() =>
        {
            var list = new ListBox { ItemsSource = Enumerable.Range(0, 40).Select(i => "组合 " + i).ToArray(), SelectedIndex = 2 };
            var page = ArrangePage(list);
            page.ScrollToVerticalOffset(100);
            page.UpdateLayout();
            var before = page.VerticalOffset;

            var wheel = Wheel(delta);
            MouseWheelRouting.ForwardToAncestorScrollViewer(list, wheel);
            page.UpdateLayout();

            Assert.IsTrue(wheel.Handled);
            Assert.IsTrue(delta < 0 ? page.VerticalOffset > before : page.VerticalOffset < before);
            Assert.AreEqual(2, list.SelectedIndex);
        });
    }

    [TestMethod]
    public void ClosedDropdownScrollsTheEditorWithoutChangingTheSelection()
    {
        RunSta(() =>
        {
            var combo = new ComboBox { ItemsSource = new[] { "保留当前壁纸", "壁纸一", "壁纸二" }, SelectedIndex = 1 };
            var page = ArrangePage(combo);
            var wheel = Wheel(-120);

            MouseWheelRouting.ForwardToAncestorScrollViewer(combo, wheel);
            page.UpdateLayout();

            Assert.IsTrue(wheel.Handled);
            Assert.IsTrue(page.VerticalOffset > 0);
            Assert.AreEqual(1, combo.SelectedIndex);
        });
    }

    [TestMethod]
    public void OpenDropdownKeepsItsOwnWheelEvent()
    {
        RunSta(() =>
        {
            var combo = new ComboBox { ItemsSource = Enumerable.Range(0, 40).ToArray(), SelectedIndex = 1 };
            var page = ArrangePage(combo);
            var host = new Window { Content = page, Width = 400, Height = 200, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            host.Show();
            combo.IsDropDownOpen = true;
            try
            {
                Assert.IsTrue(combo.IsDropDownOpen, "The dropdown must be loaded before WPF allows it to open.");
                var wheel = Wheel(-120);
                MouseWheelRouting.ForwardToAncestorScrollViewer(combo, wheel);

                Assert.IsFalse(wheel.Handled);
                Assert.AreEqual(0d, page.VerticalOffset);
            }
            finally { combo.IsDropDownOpen = false; host.Close(); }
        });
    }

    private static ScrollViewer ArrangePage(Control control)
    {
        var content = new StackPanel();
        content.Children.Add(control);
        content.Children.Add(new Border { Height = 800 });
        var page = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        page.Measure(new Size(400, 200));
        page.Arrange(new Rect(0, 0, 400, 200));
        page.UpdateLayout();
        Assert.IsTrue(page.ScrollableHeight > 0);
        return page;
    }

    private static MouseWheelEventArgs Wheel(int delta)
        => new(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };

    private static void RunSta(Action test)
        => WpfTestThread.Run(test);
}
