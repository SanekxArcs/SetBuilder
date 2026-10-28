using System.Windows.Controls;
using TunerFest.Ui.ViewModels;

namespace TunerFest.Ui.Pages;

public partial class ZonesPage : UserControl
{
    public ZonesPage(ZonesViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}
