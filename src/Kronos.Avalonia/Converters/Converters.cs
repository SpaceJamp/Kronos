using System;
using Avalonia.Data.Converters;

namespace Kronos.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is Kronos.Models.DllStatus status)
        {
            return status switch
            {
                Kronos.Models.DllStatus.Original => "#4CAF50",      // Green
                Kronos.Models.DllStatus.Swapped => "#2196F3",       // Blue
                Kronos.Models.DllStatus.Missing => "#F44336",       // Red
                Kronos.Models.DllStatus.Unknown => "#FF9800",       // Orange
                _ => "#9E9E9E"                                      // Grey
            };
        }
        return "#9E9E9E";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is true ? true : false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is true ? false : true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b && parameter is string s)
        {
            var parts = s.Split(';');
            return b ? parts[1] : parts[0];
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b && parameter is string s)
        {
            var parts = s.Split(';');
            return b ? parts[0] : parts[1];
        }
        return "#9E9E9E";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}