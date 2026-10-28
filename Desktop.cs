using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace TunerFest;

public record GameWindow(IntPtr Handle, string Title, string ProcessName)
{
    public override string ToString() => Title;
}

// Distinct type so screen-wait loops never treat a focus timeout as a retryable read error.
public sealed class FocusLostException(string message) : InvalidOperationException(message);

public static class Desktop
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr h, out RECT r);

    [DllImport("user32.dll")]
    static extern bool ClientToScreen(IntPtr h, ref POINT p);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr h);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr h, StringBuilder b, int n);

    delegate bool EnumCallback(IntPtr h, IntPtr p);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumCallback cb, IntPtr p);

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr h, int id);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int L,
            T,
            R,
            B;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X,
            Y;
    }

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    static string ProcessName(IntPtr h)
    {
        try
        {
            GetWindowThreadProcessId(h, out var pid);
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    public static bool IsGame(IntPtr h) => ProcessName(h).Contains("Motorfest", StringComparison.OrdinalIgnoreCase);

    public static List<GameWindow> Windows()
    {
        var list = new List<GameWindow>();
        EnumWindows(
            (h, _) =>
            {
                var b = new StringBuilder(512);
                GetWindowText(h, b, 512);
                if (IsWindowVisible(h) && b.Length > 0)
                    list.Add(new(h, b.ToString(), ProcessName(h)));
                return true;
            },
            IntPtr.Zero
        );
        return list;
    }

    public static Rectangle Bounds(IntPtr h)
    {
        if (!GetClientRect(h, out var r))
            throw new InvalidOperationException("Вікно гри недоступне.");
        var p = new POINT();
        if (!ClientToScreen(h, ref p) || r.R < 100 || r.B < 100)
            throw new InvalidOperationException("Вікно гри згорнуте.");
        return new(p.X, p.Y, r.R, r.B);
    }

    public static void Check(IntPtr h)
    {
        if (!IsGame(h))
            throw new InvalidOperationException("Обране вікно не належить The Crew Motorfest.");
        if (GetForegroundWindow() != h)
            throw new FocusLostException("Зупинено: гра втратила фокус.");
    }

    // The bitmap always has the full client size so zone coordinates stay valid.
    // With a region, only those pixels are copied from the screen; the rest stays black.
    public static Bitmap Capture(IntPtr h, Rectangle? region = null)
    {
        var r = Bounds(h);
        var full = new Rectangle(Point.Empty, r.Size);
        var area = region.HasValue ? Rectangle.Intersect(region.Value, full) : full;
        if (area.Width <= 0 || area.Height <= 0)
            area = full;
        var b = new Bitmap(r.Width, r.Height);
        try
        {
            using var g = Graphics.FromImage(b);
            g.CopyFromScreen(r.X + area.X, r.Y + area.Y, area.X, area.Y, area.Size);
            return b;
        }
        catch
        {
            b.Dispose();
            throw;
        }
    }
}

public sealed class Input : IDisposable
{
    public bool LastSent { get; private set; }
    readonly IntPtr window;
    readonly Profile profile;
    readonly SessionLog? trace;
    ViGEmClient? client;
    IXbox360Controller? pad;

    public Input(IntPtr target, Profile p, SessionLog? diagnostics = null)
    {
        window = target;
        profile = p;
        trace = diagnostics;
        if (!p.Xbox)
            return;
        try
        {
            client = new ViGEmClient();
            pad = client.CreateXbox360Controller();
            pad.AutoSubmitReport = true;
            pad.Connect();
        }
        catch (Exception e)
        {
            Dispose();
            throw new InvalidOperationException(
                "Віртуальний Xbox недоступний. Потрібен встановлений ViGEmBus. Оберіть клавіатуру або встановіть драйвер окремо.",
                e
            );
        }
    }

    public void ValidateRecalibrate()
    {
        if (profile.Xbox && profile.XboxRecalibrate != "KeyboardF9")
            return;
        if (!Enum.TryParse<Keys>(profile.Recalibrate, true, out var key))
            throw new InvalidOperationException("Невірна клавіша відкриття RECALIBER.");
        const int testId = 19237;
        if (!Desktop.RegisterHotKey(IntPtr.Zero, testId, 0x4000, (uint)key))
            throw new InvalidOperationException(
                "Клавіша "
                    + key
                    + " зайнята іншою програмою. Закрий стару версію Set Builder або зміни клавішу відкриття перків."
            );
        Desktop.UnregisterHotKey(IntPtr.Zero, testId);
    }

    static Xbox360Button PadButton(string name) =>
        name switch
        {
            "Start" => Xbox360Button.Start,
            "Back" => Xbox360Button.Back,
            "X" => Xbox360Button.X,
            "Y" => Xbox360Button.Y,
            "LeftShoulder" => Xbox360Button.LeftShoulder,
            "RightShoulder" => Xbox360Button.RightShoulder,
            "LeftThumb" => Xbox360Button.LeftThumb,
            "RightThumb" => Xbox360Button.RightThumb,
            _ => throw new InvalidOperationException("Оберіть кнопку Xbox для відкриття RECALIBER."),
        };

    public async Task Press(string action, CancellationToken ct)
    {
        LastSent = false;
        ct.ThrowIfCancellationRequested();
        Desktop.Check(window);
        int delayMs = PressTiming.NextDelay(profile);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        trace?.Write(
            "input_requested",
            new
            {
                action,
                backend = pad != null && !(action == "Recalibrate" && profile.XboxRecalibrate == "KeyboardF9")
                    ? "Xbox"
                    : "keyboard",
                holdMs = 65,
                delayMs,
                randomPause = profile.RandomPause,
                pauseMinMs = profile.PauseMinMs,
                pauseMaxMs = profile.PauseMaxMs,
            }
        );
        if (pad != null && !(action == "Recalibrate" && profile.XboxRecalibrate == "KeyboardF9"))
        {
            var button = action switch
            {
                "Recalibrate" => PadButton(profile.XboxRecalibrate),
                "Confirm" => Xbox360Button.A,
                "Back" => Xbox360Button.B,
                "Left" => Xbox360Button.Left,
                "Right" => Xbox360Button.Right,
                "Up" => Xbox360Button.Up,
                "Down" => Xbox360Button.Down,
                _ => throw new ArgumentException(action),
            };
            pad.SetButtonState(button, true);
            LastSent = true;
            trace?.Write("input_down", new { action, button = button.ToString() });
            try
            {
                await Task.Delay(65, ct);
            }
            finally
            {
                pad.SetButtonState(button, false);
                trace?.Write("input_up", new { action });
            }
        }
        else
        {
            var key = action switch
            {
                "Recalibrate" => profile.Recalibrate,
                "Confirm" => profile.Confirm,
                "Back" => profile.Back,
                "Left" => profile.Left,
                "Right" => profile.Right,
                "Up" => profile.Up,
                "Down" => profile.Down,
                _ => throw new ArgumentException(action),
            };
            if (!Enum.TryParse<Keys>(key, true, out var vk) || vk < Keys.Back || vk > Keys.OemClear)
                throw new InvalidOperationException($"Невідома клавіша: {key}");
            uint scan = MapVirtualKey((uint)vk, 0);
            uint flags = 8u | (vk is Keys.Left or Keys.Right or Keys.Up or Keys.Down ? 1u : 0u);
            var down = Key((ushort)scan, flags);
            var up = Key((ushort)scan, flags | 2);
            if (SendInput(1, [down], Marshal.SizeOf<INPUT>()) != 1)
                throw new InvalidOperationException(
                    "Windows не дозволяє введення. Перевірте рівні прав гри й програми."
                );
            LastSent = true;
            trace?.Write(
                "input_down",
                new
                {
                    action,
                    key,
                    scan,
                    flags,
                }
            );
            try
            {
                await Task.Delay(65, ct);
            }
            finally
            {
                var released = SendInput(1, [up], Marshal.SizeOf<INPUT>());
                trace?.Write("input_up", new { action, released });
            }
        }
        await Task.Delay(delayMs, ct);
        // Focus after the press is verified by the caller, which can pause instead of aborting.
        trace?.Write(
            "input_completed",
            new
            {
                action,
                delayMs,
                elapsedMs = watch.ElapsedMilliseconds,
            }
        );
    }

    static INPUT Key(ushort scan, uint flags) =>
        new()
        {
            Type = 1,
            Data = new UNION
            {
                Keyboard = new() { Scan = scan, Flags = flags },
            },
        };

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint code, uint type);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint count, INPUT[] data, int size);

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint Type;
        public UNION Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct UNION
    {
        [FieldOffset(0)]
        public KEYBOARD Keyboard;

        [FieldOffset(0)]
        public MOUSE Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBOARD
    {
        public ushort Vk,
            Scan;
        public uint Flags,
            Time;
        public UIntPtr Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSE
    {
        public int X,
            Y;
        public uint Data,
            Flags,
            Time;
        public UIntPtr Extra;
    }

    public void Dispose()
    {
        if (pad != null)
        {
            try
            {
                pad.ResetReport();
                pad.Disconnect();
            }
            finally
            {
                pad = null;
            }
        }
        client?.Dispose();
        client = null;
    }
}
