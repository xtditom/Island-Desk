namespace IslandDesk.Presentation;

internal static class DashboardLayout
{
    public const double Width = 400, HomeHeight = 318, SystemHeight = 392;
    // Normal desktops retain the designed size. Constrained/high-DPI working areas
    // scale the persistent layout once on display changes, avoiding cropped cards.
    public static double ScaleToFit(double availableWidth, double availableHeight, double maximumHeight = SystemHeight) =>
        Math.Min(1, Math.Min(Math.Max(1, availableWidth) / Width, Math.Max(1, availableHeight) / maximumHeight));
}
