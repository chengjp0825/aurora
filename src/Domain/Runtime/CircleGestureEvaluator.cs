using System;
using System.Collections.Generic;

namespace MyQuicker.Domain.Runtime;

/// <summary>
/// 画圈几何判定：纯函数，无状态，与输入源无关。
/// 复用原有 GestureHelper 的算法，但使用领域 <see cref="Point"/>。
/// </summary>
internal static class CircleGestureEvaluator
{
    private const double MinAspect = 0.5;
    private const double MaxAspect = 2.0;
    private const double MinVectorLen = 2.0;
    private const int MinPoints = 8;

    public static bool IsCircle(IReadOnlyList<Point> recentPoints, double minBoxSide, double minTotalTurnDeg)
    {
        if (recentPoints is null || recentPoints.Count < MinPoints)
            return false;

        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        for (int i = 0; i < recentPoints.Count; i++)
        {
            var p = recentPoints[i];
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        double width = maxX - minX;
        double height = maxY - minY;
        if (width < minBoxSide || height < minBoxSide)
            return false;

        double aspect = width / height;
        if (aspect < MinAspect || aspect > MaxAspect)
            return false;

        double totalTurn = 0.0;
        double prevAngle = double.NaN;
        for (int i = 1; i < recentPoints.Count; i++)
        {
            double dx = recentPoints[i].X - recentPoints[i - 1].X;
            double dy = recentPoints[i].Y - recentPoints[i - 1].Y;
            if (dx * dx + dy * dy < MinVectorLen * MinVectorLen)
                continue;

            double angle = Math.Atan2(dy, dx);
            if (!double.IsNaN(prevAngle))
            {
                double delta = angle - prevAngle;
                if (delta > Math.PI) delta -= 2 * Math.PI;
                else if (delta < -Math.PI) delta += 2 * Math.PI;
                totalTurn += delta;
            }
            prevAngle = angle;
        }

        return Math.Abs(totalTurn) * (180.0 / Math.PI) >= minTotalTurnDeg;
    }
}
