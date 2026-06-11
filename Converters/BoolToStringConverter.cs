using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SerialPortTool.Converters;

public class BoolToStringConverter : IValueConverter
{
    public string TrueValue { get; set; } = "是";
    public string FalseValue { get; set; } = "否";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? TrueValue : FalseValue;
        return FalseValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToOpenCloseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? "关闭" : "打开";
        return "打开";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? "#4CAF50" : "#F44336";
        return "#F44336";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>串口关闭=绿色背景（打开），串口打开=红色背景（关闭）</summary>
public class BoolToPortButtonBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ClosedBrush = CreateBrush("#C8E6C9");
    private static readonly SolidColorBrush OpenBrush = CreateBrush("#FFCDD2");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isOpen)
            return isOpen ? OpenBrush : ClosedBrush;
        return ClosedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}

public class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}