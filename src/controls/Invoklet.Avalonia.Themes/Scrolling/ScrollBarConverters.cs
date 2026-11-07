using Avalonia.Data.Converters;

namespace Invoklet.Avalonia.Themes.Scrolling;

public static class ScrollBarConverters
{
    public static readonly IValueConverter HasRange = new FuncValueConverter<double, bool>(maximum => maximum > 0);

    /// <summary>The most of a short bar the thumb may take, so that it can still move.</summary>
    public static readonly IValueConverter Half = new FuncValueConverter<double, double>(length => length / 2);
}
