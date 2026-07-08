using System;
using System.Globalization;
using System.Windows.Data;

namespace Aurora.Converters;

/// <summary>
/// Segoe MDL2 Assets 图标码（hex 字符串如 "EFA8"）→ 字形字符。
/// 用于动作按钮图标显示：DataGrid 编辑 hex 码，菜单按钮显示对应字形。
/// 空/无效回退默认占位字（U+EFA8）。
/// </summary>
public class HexToGlyphConverter : IValueConverter
{
    // 用 ConvertFromUtf32 构造，避免源码里出现不可见的 Private Use 字形字符。
    private static readonly string DefaultGlyph = char.ConvertFromUtf32(0xEFA8);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s) &&
            int.TryParse(s.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
        {
            try { return char.ConvertFromUtf32(code); }
            catch { /* 码点非法（如代理项）→ 回退 */ }
        }
        return DefaultGlyph;
    }

    /// <summary>单向转换：编辑模板直接绑 hex 字符串，不走转换器回写。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}
