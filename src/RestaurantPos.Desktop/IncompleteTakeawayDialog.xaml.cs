using System.Windows;

namespace RestaurantPos.Desktop;

public partial class IncompleteTakeawayDialog : Window
{
    public IncompleteTakeawayDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => ReturnToOrderButton.Focus();
    }
}
