namespace Aurora.Domain.Runtime;

/// <summary>显示器物理像素边界。</summary>
public sealed record ScreenBounds(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(Point point) =>
        point.X >= X && point.X < Right &&
        point.Y >= Y && point.Y < Bottom;
}
