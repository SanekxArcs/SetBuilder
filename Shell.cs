using System.Runtime.InteropServices;
using Microsoft.Toolkit.Uwp.Notifications;

namespace TunerFest;

// Single-instance activation of the running window.
public static class Shell
{
    public const string WindowTitlePrefix = Ui.AppInfo.TitlePrefix;

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr h);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr h, int cmd);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr h);

    public static void Activate(IntPtr handle)
    {
        if (IsIconic(handle))
            ShowWindow(handle, 9);
        SetForegroundWindow(handle);
    }

    // Second launch: bring the running window to the front instead of starting another instance.
    public static bool ActivateExisting()
    {
        var existing = Desktop
            .Windows()
            .FirstOrDefault(w => w.Title.StartsWith(WindowTitlePrefix, StringComparison.Ordinal));
        if (existing == null)
            return false;
        Activate(existing.Handle);
        return true;
    }
}

// Windows toast notifications. Every call is best-effort: a missing notification platform never breaks a run.
public static class Toasts
{
    public static bool Enabled { get; set; } = true;

    public static void Show(string title, string text, string tag)
    {
        if (!Enabled)
            return;
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(text)
                .Show(toast =>
                {
                    toast.Tag = tag;
                    toast.Group = "setbuilder";
                });
        }
        catch (Exception) { }
    }

    public static void Remove(string tag)
    {
        try
        {
            ToastNotificationManagerCompat.History.Remove(tag, "setbuilder");
        }
        catch (Exception) { }
    }
}
