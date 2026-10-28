using System.Windows.Controls;
using TunerFest.Ui.ViewModels;

namespace TunerFest.Ui.Pages;

public partial class TemplatePage : UserControl
{
    public TemplatePage(TemplateViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}
