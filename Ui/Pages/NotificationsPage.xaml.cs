using System.Windows.Controls;
using TunerFest.Ui.ViewModels;

namespace TunerFest.Ui.Pages;

public partial class NotificationsPage : UserControl
{
    public NotificationsPage(NotificationsViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}
