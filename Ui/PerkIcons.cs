using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TunerFest.Ui.ViewModels;
using Color = System.Windows.Media.Color;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace TunerFest.Ui;

/// <summary>
/// Perk, set and build icons for the build page: Assets/&lt;title without punctuation, lower case&gt;.svg rendered at the
/// requested pixel size and recoloured to the current accent (alpha kept); .png is the fallback and is used as is.
/// Icons are frozen bitmaps cached per title, size and accent, so they can be shared by every row and open list.
/// </summary>
public static class PerkIcons
{
    static readonly Dictionary<string, ImageSource?> cache = [];

    /// <summary>The asset title of an item: a build shows the icon of its set; "Власна збірка" and "— без альтернативи —" have none.</summary>
    public static string? IconTitle(object? item) =>
        item switch
        {
            Build { Set: "" } => null,
            Build b => b.Set,
            Affix a when ReferenceEquals(a, BuildViewModel.NoAlternative) => null,
            null => null,
            _ => item.ToString(),
        };

    public static ImageSource? For(string? title, int size)
    {
        if (string.IsNullOrEmpty(title) || size <= 0)
            return null;
        var accent = Accent();
        string key = title + "@" + size + "@" + accent;
        lock (cache)
        {
            if (cache.TryGetValue(key, out var cached))
                return cached;
        }
        ImageSource? image = Render(title, size, accent);
        lock (cache)
            cache[key] = image;
        return image;
    }

    static ImageSource? Render(string title, int size, Color accent)
    {
        var name = System.Text.RegularExpressions.Regex.Replace(title, "[^a-zA-Z0-9]", "").ToLowerInvariant();
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", name + ".svg");
        try
        {
            if (File.Exists(path))
                return RenderSvg(path, size, accent);
            path = Path.ChangeExtension(path, ".png");
            if (!File.Exists(path))
                return null;
            var png = new BitmapImage();
            png.BeginInit();
            png.CacheOption = BitmapCacheOption.OnLoad;
            png.UriSource = new Uri(path, UriKind.Absolute);
            png.EndInit();
            png.Freeze();
            return png;
        }
        catch
        {
            return null;
        }
    }

    static BitmapSource RenderSvg(string path, int size, Color accent)
    {
        var svg = Svg.SvgDocument.Open(path);
        svg.Width = size;
        svg.Height = size;
        using var bitmap = svg.Draw();
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppArgb);
        byte[] pixels;
        int stride;
        try
        {
            stride = data.Stride;
            pixels = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        // GDI+ 32bppArgb is B, G, R, A in memory, the same order as WPF Bgra32: keep the alpha, paint every pixel with the accent.
        for (int y = 0; y < bitmap.Height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < bitmap.Width; x++)
            {
                int i = row + x * 4;
                pixels[i] = accent.B;
                pixels[i + 1] = accent.G;
                pixels[i + 2] = accent.R;
            }
        }
        var source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        source.Freeze();
        return source;
    }

    /// <summary>The accent WPF-UI currently paints controls with; Windows blue when no theme has been applied yet.</summary>
    public static Color Accent()
    {
        try
        {
            var resources = System.Windows.Application.Current?.Resources;
            if (resources?["AccentFillColorDefaultBrush"] is SolidColorBrush brush && brush.Color.A != 0)
                return brush.Color;
            if (resources?["SystemAccentColorPrimary"] is Color primary && primary.A != 0)
                return primary;
            if (resources?["SystemAccentColor"] is Color system && system.A != 0)
                return system;
            var managed = Wpf.Ui.Appearance.ApplicationAccentColorManager.PrimaryAccent;
            if (managed.A != 0)
                return managed;
        }
        catch
        {
            // Resources are optional here; the fallback below keeps icons visible.
        }
        return Color.FromRgb(0x00, 0x78, 0xD4);
    }
}

/// <summary>Binding converter for combo box items: a catalog item (Build, SetBonus, Affix) to its icon at <see cref="Size"/> pixels.</summary>
public sealed class PerkIconConverter : IValueConverter
{
    public int Size { get; set; } = 20;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        PerkIcons.For(PerkIcons.IconTitle(value), Size);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
