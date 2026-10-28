using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace TunerFest.Ui.Shell;

/// <summary>Profile.WindowBounds / WindowMaximized: restored only when still visible on a connected screen, saved when the window closes.</summary>
public static class WindowPlacement
{
    public static void Restore(Window window, Profile profile)
    {
        var saved = profile.WindowBounds;
        if (saved is { Length: 4 })
        {
            var bounds = new Rect(saved[0], saved[1], saved[2], saved[3]);
            double scale = 1;
            try
            {
                scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
            }
            catch (Exception) { }
            if (scale <= 0)
                scale = 1;
            bool visible = false;
            try
            {
                // Only reuse a placement that still overlaps the working area of some screen (WinForms screens are in pixels).
                visible = Forms.Screen.AllScreens.Any(s =>
                {
                    var area = s.WorkingArea;
                    var dips = new Rect(area.X / scale, area.Y / scale, area.Width / scale, area.Height / scale);
                    var overlap = Rect.Intersect(dips, bounds);
                    return !overlap.IsEmpty && overlap.Width > 200 && overlap.Height > 120;
                });
            }
            catch (Exception) { }
            if (visible && bounds.Width >= window.MinWidth && bounds.Height >= window.MinHeight)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = bounds.X;
                window.Top = bounds.Y;
                window.Width = bounds.Width;
                window.Height = bounds.Height;
            }
        }
        if (profile.WindowMaximized)
            window.WindowState = WindowState.Maximized;
    }

    public static void Save(Window window, Profile profile)
    {
        var bounds =
            window.WindowState == WindowState.Normal
                ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
                : window.RestoreBounds;
        if (bounds.IsEmpty || double.IsNaN(bounds.X) || double.IsNaN(bounds.Y))
            return;
        profile.WindowBounds = [(int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height];
        profile.WindowMaximized = window.WindowState == WindowState.Maximized;
        try
        {
            profile.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
