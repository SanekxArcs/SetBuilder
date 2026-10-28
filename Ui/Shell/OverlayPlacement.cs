namespace TunerFest.Ui.Shell;

/// <summary>
/// Where the overlay panel may sit without touching anything a screen read looks at. Every zone starts at
/// 18.4 % of the game width or further right (Profile.Defaults), and Runner.Region inflates a zone union by
/// 24 px before it copies pixels from the screen, so the strip left of that is free for the whole height.
/// All values are screen pixels, like Desktop.Bounds and Zone.Pixels.
/// </summary>
public static class OverlayPlacement
{
    /// <summary>Gap between the game's client corner and the panel.</summary>
    public const int Margin = 12;

    /// <summary>Runner.Region inflates the captured area by this much around the zones.</summary>
    public const int CaptureInflate = 24;

    /// <summary>Extra distance kept from the inflated capture area.</summary>
    public const int Safety = 8;

    /// <summary>Below this the panel is unusable; a smaller game window gets this width and relies on capture exclusion.</summary>
    public const int MinWidth = 180;

    /// <summary>Width of the strip at the left edge of the game that no read can touch (zones inflated by the capture margin).</summary>
    public static int FreeWidth(Size game, IEnumerable<Zone> zones)
    {
        double minX = zones.Select(z => z.X).DefaultIfEmpty(1).Min();
        return (int)Math.Floor(minX * game.Width) - CaptureInflate - Safety;
    }

    /// <summary>
    /// The panel rectangle: Margin from the game's top-left client corner, as wide as preferred but never past the free strip,
    /// never taller than the game. Width stays at least MinWidth.
    /// </summary>
    public static Rectangle Place(Rectangle game, IEnumerable<Zone> zones, int preferredWidth, int height)
    {
        int limit = FreeWidth(game.Size, zones) - Margin;
        int width = Math.Max(MinWidth, Math.Min(preferredWidth, limit));
        int tall = Math.Max(1, Math.Min(height, game.Height - 2 * Margin));
        return new Rectangle(game.X + Margin, game.Y + Margin, width, tall);
    }

    /// <summary>True when the panel overlaps any zone's captured area (zone pixels inflated by the capture margin).</summary>
    public static bool TouchesZones(Rectangle panel, Rectangle game, IEnumerable<Zone> zones)
    {
        foreach (var zone in zones)
        {
            var area = zone.Pixels(game.Size);
            area.Offset(game.Location);
            area.Inflate(CaptureInflate, CaptureInflate);
            if (area.IntersectsWith(panel))
                return true;
        }
        return false;
    }
}
