using System.Drawing;
using System.Threading.Tasks;
using Aurora.Domain.Runtime;

namespace Aurora.Tests.Fakes;

/// <summary>IScreenshotOverlay 的手写假实现，用于 ScreenshotWorkflow 测试。</summary>
internal sealed class FakeScreenshotOverlay : IScreenshotOverlay
{
    public Rectangle? NextResult { get; set; }
    public Bitmap? LastFullImage { get; private set; }
    public Rectangle? LastFullBounds { get; private set; }
    public int CallCount { get; private set; }

    public Task<Rectangle?> SelectRegionAsync(Bitmap fullImage, Rectangle fullBounds)
    {
        CallCount++;
        LastFullImage = fullImage;
        LastFullBounds = fullBounds;
        return Task.FromResult(NextResult);
    }
}
