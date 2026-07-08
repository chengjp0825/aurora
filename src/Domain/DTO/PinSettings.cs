namespace Aurora.Domain.DTO;

/// <summary>贴图窗口参数。仅包含纯数据。</summary>
public sealed class PinSettings
{
    /// <summary>"显示边界"开启时的边框颜色。</summary>
    public string BorderColor { get; set; } = "Gray";

    /// <summary>贴图默认不透明度。</summary>
    public double DefaultOpacity { get; set; } = 1.0;

    /// <summary>贴图默认是否显示边界（钉图时默认开启 2px 边框，向外生长）。</summary>
    public bool DefaultShowBorder { get; set; } = true;

    /// <summary>贴图默认是否开启批注模式（Hover 工具栏）。默认关闭。</summary>
    public bool DefaultAnnotationMode { get; set; } = false;

    /// <summary>贴图默认是否置顶。默认开启。</summary>
    public bool DefaultTopmost { get; set; } = true;

    /// <summary>贴图默认是否显示阴影。默认开启。</summary>
    public bool DefaultShowShadow { get; set; } = true;
}
