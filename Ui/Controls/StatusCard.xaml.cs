using System.Windows;
using System.Windows.Controls;

namespace TunerFest.Ui.Controls;

/// <summary>Header card bound to a StatusCardViewModel; Actions holds the start/stop buttons placed on its right side.</summary>
public partial class StatusCard : UserControl
{
    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions),
        typeof(object),
        typeof(StatusCard)
    );

    public StatusCard() => InitializeComponent();

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}
