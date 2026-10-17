namespace IslandDesk.Settings;

internal sealed record FrameRateOption(int? Rate, bool IsAvailable)
{
    public string Label => Rate is null ? "Max" : $"{Rate} FPS" + (IsAvailable ? "" : " · Unavailable");
    public override string ToString() => Label;
    public static FrameRateOption[] ForDisplay(int? refreshRate) =>
        new int?[] { 30, 60, 90, 120, 180, 270, 360, null }
            .Select(rate => new FrameRateOption(rate, rate is null || (refreshRate is { } hz && rate <= hz))).ToArray();
}
