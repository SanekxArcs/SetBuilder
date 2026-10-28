using System.Windows;
using System.Windows.Interop;

namespace TunerFest.Ui.Shell;

/// <summary>Global F8 / F7 / F10 hotkeys on the window's HWND: 8 = F8 → Arm(true), 9 = F7 → Arm(false), 10 = F10 → Cancel.</summary>
public sealed class HotkeyService : IDisposable
{
    const int WmHotkey = 0x0312;
    const uint ModNoRepeat = 0x4000;
    const uint VkF7 = 0x76,
        VkF8 = 0x77,
        VkF10 = 0x79;

    readonly Window window;
    readonly MainViewModel shell;
    HwndSource? source;
    IntPtr handle;
    bool registered;

    public HotkeyService(Window window, MainViewModel shell)
    {
        this.window = window;
        this.shell = shell;
    }

    /// <summary>True when all three keys registered. Sets MainViewModel.StopAvailable from the F10 registration.</summary>
    public bool AllRegistered { get; private set; }

    /// <summary>Registers the hotkeys; the window handle must exist (call from Loaded or later).</summary>
    public void Register()
    {
        if (registered)
            return;
        handle = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(handle);
        source?.AddHook(WndProc);
        bool all = Desktop.RegisterHotKey(handle, 8, ModNoRepeat, VkF8);
        all = Desktop.RegisterHotKey(handle, 9, ModNoRepeat, VkF7) && all;
        bool stop = Desktop.RegisterHotKey(handle, 10, ModNoRepeat, VkF10);
        all = stop && all;
        registered = true;
        AllRegistered = all;
        shell.StopAvailable = stop;
        if (!all)
            shell.Log("Частина гарячих клавіш зайнята іншою програмою. Кнопки працюють; запуск має 5 секунд відліку.");
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey)
            return IntPtr.Zero;
        switch (wParam.ToInt32())
        {
            case 8:
                shell.Arm(true);
                break;
            case 9:
                shell.Arm(false);
                break;
            case 10:
                shell.Cancel();
                break;
        }
        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (registered)
        {
            foreach (int id in new[] { 8, 9, 10 })
                Desktop.UnregisterHotKey(handle, id);
            registered = false;
        }
        source?.RemoveHook(WndProc);
        source = null;
    }
}
