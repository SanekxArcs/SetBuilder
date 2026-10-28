using System.Windows.Controls;
using TunerFest.Ui.ViewModels;

namespace TunerFest.Ui.Pages;

public partial class BuildPage : UserControl
{
    public BuildPage(BuildViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}
