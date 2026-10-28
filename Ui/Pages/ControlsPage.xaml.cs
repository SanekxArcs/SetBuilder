using System.Windows.Controls;
using TunerFest.Ui.ViewModels;

namespace TunerFest.Ui.Pages;

public partial class ControlsPage : UserControl
{
    public ControlsPage(ControlsViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}
