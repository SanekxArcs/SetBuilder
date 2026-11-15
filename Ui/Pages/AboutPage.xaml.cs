using System.Windows.Controls;
using TunerFest.Ui.ViewModels;

namespace TunerFest.Ui.Pages;

public partial class AboutPage : UserControl
{
    public AboutPage(AboutViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}
