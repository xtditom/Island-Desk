namespace IslandDesk.Themes;

// Light surfaces need a quieter desktop contribution. Keep the preference live,
// but map 60–100% to 90–100% in Light; nested cards remain at least 96% opaque.
internal static class SurfaceContrast
{
    internal static byte Alpha(double opacity, bool dark, bool card = false)
    {
        var fraction = Math.Clamp(opacity, 60, 100) / 100;
        if (!dark) fraction = Math.Max(card ? .96 : .90, .75 + .25 * fraction);
        return (byte)Math.Round(fraction * 255);
    }
}
