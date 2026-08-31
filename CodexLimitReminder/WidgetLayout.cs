namespace CodexLimitReminder;

internal readonly record struct WidgetSize(int Width, int Height);

internal readonly record struct WidgetRectangle(int Left, int Top, int Right, int Bottom)
{
    internal int Width => Right - Left;
    internal int Height => Bottom - Top;
}

internal static class WidgetLayout
{
    internal const int EdgeMargin = 16;

    internal static WidgetSize GetLogicalSize(bool expanded, int limitCount, int estimatedGroupLines = 0) => expanded
        ? new WidgetSize(
            348,
            Math.Clamp(
                66 + Math.Max(1, limitCount) * 50 + Math.Max(0, estimatedGroupLines) * 16,
                166,
                520))
        : new WidgetSize(260, 84);

    internal static WidgetRectangle PlaceAtBottomRight(WidgetSize size, WidgetRectangle workArea) =>
        Clamp(
            new WidgetRectangle(
                workArea.Right - size.Width - EdgeMargin,
                workArea.Bottom - size.Height - EdgeMargin,
                workArea.Right - EdgeMargin,
                workArea.Bottom - EdgeMargin),
            workArea);

    internal static WidgetRectangle ResizeFromBottomRight(
        WidgetRectangle current,
        WidgetSize size,
        WidgetRectangle workArea) =>
        Clamp(
            new WidgetRectangle(
                current.Right - size.Width,
                current.Bottom - size.Height,
                current.Right,
                current.Bottom),
            workArea);

    internal static WidgetRectangle PlaceSaved(int x, int y, WidgetSize size, WidgetRectangle workArea) =>
        Clamp(new WidgetRectangle(x, y, x + size.Width, y + size.Height), workArea);

    private static WidgetRectangle Clamp(WidgetRectangle rectangle, WidgetRectangle workArea)
    {
        int width = Math.Min(rectangle.Width, workArea.Width);
        int height = Math.Min(rectangle.Height, workArea.Height);
        int left = Math.Clamp(rectangle.Left, workArea.Left, workArea.Right - width);
        int top = Math.Clamp(rectangle.Top, workArea.Top, workArea.Bottom - height);
        return new WidgetRectangle(left, top, left + width, top + height);
    }
}
