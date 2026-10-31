using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Cleaner.App;

/// <summary>true → Visible; с параметром "invert" — наоборот.</summary>
public class BoolToVis : IValueConverter
{
    public object Convert(object value, Type t, object? p, CultureInfo c) =>
        (value is true) ^ (p as string == "invert") ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>bool → !bool.</summary>
public class NotConverter : IValueConverter
{
    public object Convert(object value, Type t, object? p, CultureInfo c) => value is not true;

    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => value is not true;
}

/// <summary>Не null и не пустая строка → Visible.</summary>
public class NotNullToVis : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is null or "" ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Число больше нуля → Visible.</summary>
public class NotZeroToVis : IValueConverter
{
    public object Convert(object value, Type t, object? p, CultureInfo c) =>
        value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
