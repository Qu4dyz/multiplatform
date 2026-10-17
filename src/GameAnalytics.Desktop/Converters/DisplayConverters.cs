using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GameAnalytics.Core.Enums;

namespace GameAnalytics.Desktop.Converters;

/// <summary>Blue/Red team accent used across match lists.</summary>
public sealed class TeamSideBrushConverter : IValueConverter
{
    public static readonly TeamSideBrushConverter Instance = new();

    public static readonly IBrush BlueBrush = new SolidColorBrush(Color.Parse("#5383E8"));
    public static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#E84057"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is TeamSide.Red ? RedBrush : BlueBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Match times are stored as UTC; SQLite hands them back with Kind=Unspecified.
/// Shows them in the viewer's local time.
/// </summary>
public sealed class UtcToLocalTimeConverter : IValueConverter
{
    public static readonly UtcToLocalTimeConverter Instance = new();

    public static DateTime ToLocal(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime()
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime dt) return value;
        var format = parameter as string ?? "yyyy-MM-dd HH:mm";
        return ToLocal(dt).ToString(format, culture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
