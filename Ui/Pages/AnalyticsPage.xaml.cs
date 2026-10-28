using System.Windows;
using System.Windows.Controls;
using TunerFest.Ui.ViewModels;

namespace TunerFest.Ui.Pages;

public partial class AnalyticsPage : UserControl
{
    readonly AnalyticsViewModel vm;

    public AnalyticsPage(AnalyticsViewModel vm)
    {
        this.vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    // The logs are read the first time the page is shown, and again when a run finished while it was hidden.
    void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => vm.IsVisible = e.NewValue is true;
}
