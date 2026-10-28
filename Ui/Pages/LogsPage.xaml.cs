using System.Windows.Controls;
using TunerFest.Ui.ViewModels;

namespace TunerFest.Ui.Pages;

public partial class LogsPage : UserControl
{
    public LogsPage(LogsViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}
