using System.Windows;

namespace RestaurantPos.Desktop;

public partial class OrderCancellationDialog : Window
{
    public OrderCancellationDialog(string orderContext)
    {
        InitializeComponent();
        OrderContextText.Text = orderContext;
        Loaded += (_, _) => KeepOrderButton.Focus();
    }

    private void ConfirmCancellation_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
