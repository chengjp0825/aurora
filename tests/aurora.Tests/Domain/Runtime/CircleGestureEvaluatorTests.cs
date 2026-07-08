using System;
using System.Collections.Generic;
using Aurora.Domain.Runtime;
using Xunit;

namespace Aurora.Tests.Domain.Runtime;

public class CircleGestureEvaluatorTests
{
    [Fact]
    public void IsCircle_NullOrTooFewPoints_ReturnsFalse()
    {
        Assert.False(CircleGestureEvaluator.IsCircle(null!, 80.0, 300.0));
        Assert.False(CircleGestureEvaluator.IsCircle(new List<Point>(), 80.0, 300.0));
        Assert.False(CircleGestureEvaluator.IsCircle(CreateCirclePoints(7, 100), 80.0, 300.0));
    }

    [Fact]
    public void IsCircle_SmallBoundingBox_ReturnsFalse()
    {
        var tinyCircle = CreateCirclePoints(20, 10);

        Assert.False(CircleGestureEvaluator.IsCircle(tinyCircle, 80.0, 300.0));
    }

    [Fact]
    public void IsCircle_WrongAspectRatio_ReturnsFalse()
    {
        var flattened = CreateEllipsePoints(20, 200, 20);

        Assert.False(CircleGestureEvaluator.IsCircle(flattened, 80.0, 300.0));
    }

    [Fact]
    public void IsCircle_InsufficientTurn_ReturnsFalse()
    {
        var arc = CreateArcPoints(20, 100, 90.0);

        Assert.False(CircleGestureEvaluator.IsCircle(arc, 80.0, 300.0));
    }

    [Fact]
    public void IsCircle_ValidCircle_ReturnsTrue()
    {
        var circle = CreateCirclePoints(32, 100);

        Assert.True(CircleGestureEvaluator.IsCircle(circle, 80.0, 300.0));
    }

    [Fact]
    public void IsCircle_LowerSensitivityThreshold_MatchesMoreEasily()
    {
        // Radius 35 gives a bounding box of ~70, which satisfies High (60) but not Medium (80).
        var smallCircle = CreateCirclePoints(20, 35);

        Assert.True(CircleGestureEvaluator.IsCircle(smallCircle, 60.0, 270.0));
        Assert.False(CircleGestureEvaluator.IsCircle(smallCircle, 80.0, 300.0));
    }

    [Fact]
    public void IsCircle_LineWithEnoughPoints_ReturnsFalse()
    {
        var line = new List<Point>();
        for (int i = 0; i < 50; i++)
            line.Add(new Point(i * 10, i * 10));

        Assert.False(CircleGestureEvaluator.IsCircle(line, 80.0, 300.0));
    }

    private static List<Point> CreateCirclePoints(int count, int radius)
    {
        var points = new List<Point>(count);
        for (int i = 0; i < count; i++)
        {
            double angle = 2 * Math.PI * i / count;
            points.Add(new Point(
                (int)(radius * Math.Cos(angle)) + radius,
                (int)(radius * Math.Sin(angle)) + radius));
        }
        return points;
    }

    private static List<Point> CreateEllipsePoints(int count, int radiusX, int radiusY)
    {
        var points = new List<Point>(count);
        for (int i = 0; i < count; i++)
        {
            double angle = 2 * Math.PI * i / count;
            points.Add(new Point(
                (int)(radiusX * Math.Cos(angle)) + radiusX,
                (int)(radiusY * Math.Sin(angle)) + radiusY));
        }
        return points;
    }

    private static List<Point> CreateArcPoints(int count, int radius, double sweepDegrees)
    {
        var points = new List<Point>(count);
        for (int i = 0; i < count; i++)
        {
            double angle = sweepDegrees * Math.PI / 180.0 * i / count;
            points.Add(new Point(
                (int)(radius * Math.Cos(angle)) + radius,
                (int)(radius * Math.Sin(angle)) + radius));
        }
        return points;
    }
}