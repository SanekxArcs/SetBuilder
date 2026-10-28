using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TunerFest.Ui;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is bool b && !b;

    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is bool b && !b;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type t, object p, CultureInfo c) =>
        (value is bool b && b) != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        (value is Visibility v && v == Visibility.Visible) != Invert;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is null or "" ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => Equals(value?.ToString(), p?.ToString());

    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        value is true ? p?.ToString() ?? "" : Binding.DoNothing;
}
