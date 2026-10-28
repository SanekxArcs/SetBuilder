using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace TunerFest.Ui.Shell;

/// <summary>Applies Profile.Theme ("system" | "dark" | "light") with the Mica backdrop and the system accent; follows Windows while "system".</summary>
public sealed class ThemeService : IDisposable
{
    string theme = "system";
    bool? appliedDark;

    public ThemeService() => SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

    public event Action<bool>? EffectiveThemeChanged;

    public bool IsDark => appliedDark ?? IsSystemDark();

    public void Apply(string? value)
    {
        theme = value?.ToLowerInvariant() switch
        {
            "dark" => "dark",
            "light" => "light",
            _ => "system",
        };
        Update(force: true);
    }

    void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (theme != "system")
            return;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Update(force: false));
    }

    void Update(bool force)
    {
        bool dark = theme switch
        {
            "dark" => true,
            "light" => false,
            _ => IsSystemDark(),
        };
        if (!force && dark == appliedDark)
            return;
        appliedDark = dark;
        ApplicationThemeManager.Apply(
            dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica,
            updateAccent: true
        );
        EffectiveThemeChanged?.Invoke(dark);
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
            );
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return true;
        }
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
