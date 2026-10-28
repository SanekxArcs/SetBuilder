using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace TunerFest.Ui;

/// <summary>A filter entry of the analytics page: the code is what Render filters by, the label is what the list shows.</summary>
public sealed record AnalyticsChoice(string Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One row of the "Рейтинг перків" grid.</summary>
public sealed record AnalyticsPerkRow(
    int Rank,
    string Perk,
    int Count,
    double Chance,
    int Numeric,
    int Max,
    double? MaxChance
);

/// <summary>One row of the "Завершені деталі" grid. Duration is the formatted text; DurationSeconds and TimeValue keep the columns sortable.</summary>
public sealed record AnalyticsItemRow(
    DateTimeOffset? TimeValue,
    string Part,
    string Set,
    double? DurationSeconds,
    int? Rolls,
    long? Bucks,
    long? Spares,
    string Session
)
{
    public string Time => TimeValue?.ToLocalTime().ToString("dd.MM HH:mm:ss") ?? "—";
    public string Duration => ItemAnalytics.Duration(DurationSeconds);
}

/// <summary>Inner tab strip of the analytics page: the button whose index equals the selected tab is Primary, the others Secondary.</summary>
public sealed class AnalyticsTabAppearanceConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        Equals(value?.ToString(), p?.ToString()) ? ControlAppearance.Primary : ControlAppearance.Secondary;

    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Visible only when every bound value is true (the empty-state hint of a grid: its tab is open and it has no rows).</summary>
public sealed class AnalyticsAllTrueVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c) =>
        values.All(v => v is true) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}
