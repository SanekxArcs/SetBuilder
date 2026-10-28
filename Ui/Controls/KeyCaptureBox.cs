using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace TunerFest.Ui.Controls;

/// <summary>
/// A focusable box that shows a key name and takes the next key pressed into it.
/// The name stored is the WinForms <see cref="Forms.Keys"/> name ("Return", "Escape", "F9", "Left"),
/// because the core (Desktop.cs, Input.Press) parses that enum. F7, F8 and F10 are the app hotkeys and are ignored.
/// </summary>
public sealed class KeyCaptureBox : ContentControl
{
    public static readonly DependencyProperty KeyNameProperty = DependencyProperty.Register(
        nameof(KeyName),
        typeof(string),
        typeof(KeyCaptureBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault)
    );

    readonly Border frame;
    readonly TextBlock text;
    readonly Wpf.Ui.Controls.SymbolIcon icon;

    public KeyCaptureBox()
    {
        Focusable = true;
        IsTabStop = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        Height = 32;
        MinWidth = 120;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        ToolTip = "Натисни клавішу, щоб призначити її";

        text = new TextBlock
        {
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(KeyName)) { Source = this });
        icon = new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = Wpf.Ui.Controls.SymbolRegular.Keyboard24,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(icon, Dock.Right);
        row.Children.Add(icon);
        row.Children.Add(text);
        frame = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 0, 10, 0),
            Child = row,
        };
        Content = frame;
        ApplyLook();

        GotKeyboardFocus += (_, _) => ApplyLook();
        LostKeyboardFocus += (_, _) => ApplyLook();
        IsEnabledChanged += (_, _) => ApplyLook();
    }

    public string KeyName
    {
        get => (string)GetValue(KeyNameProperty);
        set => SetValue(KeyNameProperty, value);
    }

    /// <summary>
    /// The WinForms Keys name for a WPF key, or null when the key is one of the app hotkeys (F7, F8, F10)
    /// or has no virtual-key code. Pass <c>e.SystemKey</c> when <c>e.Key == Key.System</c>.
    /// </summary>
    public static string? KeyNameFor(Key key)
    {
        if (key is Key.F7 or Key.F8 or Key.F10 or Key.None)
            return null;
        // Keys.Return and Keys.Enter share the value 13 and Enum.ToString picks "Enter";
        // the profile default and the core's documented names use "Return".
        if (key == Key.Return)
            return nameof(Forms.Keys.Return);
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0)
            return null;
        return ((Forms.Keys)vk).ToString();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed)
            key = e.ImeProcessedKey;
        var name = KeyNameFor(key);
        if (name is null)
        {
            base.OnPreviewKeyDown(e);
            return;
        }
        SetCurrentValue(KeyNameProperty, name);
        e.Handled = true;
    }

    void ApplyLook()
    {
        bool focused = IsKeyboardFocusWithin;
        frame.SetResourceReference(
            Border.BackgroundProperty,
            !IsEnabled ? "ControlFillColorDisabledBrush"
            : focused ? "ControlFillColorInputActiveBrush"
            : "ControlFillColorDefaultBrush"
        );
        frame.SetResourceReference(
            Border.BorderBrushProperty,
            focused ? "AccentFillColorDefaultBrush" : "ControlElevationBorderBrush"
        );
        text.SetResourceReference(
            TextBlock.ForegroundProperty,
            IsEnabled ? "TextFillColorPrimaryBrush" : "TextFillColorDisabledBrush"
        );
        icon.SetResourceReference(
            ForegroundProperty,
            !IsEnabled ? "TextFillColorDisabledBrush"
            : focused ? "AccentFillColorDefaultBrush"
            : "TextFillColorSecondaryBrush"
        );
    }
}
