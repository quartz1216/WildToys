using System.Drawing;

namespace WildToys.Modules.LumaEdges;

public static class HotZoneDetector
{
    public static HotZone Detect(Point position, Rectangle bounds, int thickness)
    {
        if (thickness <= 0 || !bounds.Contains(position))
        {
            return HotZone.None;
        }

        var x = position.X;
        var y = position.Y;

        var left = bounds.Left;
        var top = bounds.Top;
        var right = bounds.Right;
        var bottom = bounds.Bottom;

        // To make corners actually clickable when thickness is very small (like 2px),
        // we internally give corners a slightly more forgiving hit box (e.g., minimum 5px)
        int cornerHitSize = System.Math.Max(thickness, 5);

        // Corner checks
        bool cornerTop = y >= top && y <= top + cornerHitSize;
        bool cornerBottom = y <= bottom && y >= bottom - cornerHitSize;
        bool cornerLeft = x >= left && x <= left + cornerHitSize;
        bool cornerRight = x <= right && x >= right - cornerHitSize;

        // Edge checks
        bool inTopBand = y >= top && y <= top + thickness;
        bool inBottomBand = y <= bottom && y >= bottom - thickness;
        bool inLeftBand = x >= left && x <= left + thickness;
        bool inRightBand = x <= right && x >= right - thickness;

        // Corners evaluated first
        if (cornerTop && cornerLeft) return HotZone.TopLeft;
        if (cornerTop && cornerRight) return HotZone.TopRight;
        if (cornerBottom && cornerLeft) return HotZone.BottomLeft;
        if (cornerBottom && cornerRight) return HotZone.BottomRight;

        // Edges (between corners)
        if (inTopBand && x >= left && x <= right) return HotZone.Top;
        if (inBottomBand && x >= left && x <= right) return HotZone.Bottom;
        if (inLeftBand && y >= top && y <= bottom) return HotZone.Left;
        if (inRightBand && y >= top && y <= bottom) return HotZone.Right;

        return HotZone.None;
    }
}
