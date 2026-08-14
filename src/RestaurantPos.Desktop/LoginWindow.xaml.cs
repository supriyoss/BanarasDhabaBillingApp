using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application;
using RestaurantPos.Domain;

namespace RestaurantPos.Desktop;

public partial class LoginWindow : Window
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly UserSession session;
    public LoginWindow(IServiceScopeFactory scopeFactory, UserSession session)
    {
        InitializeComponent();
        this.scopeFactory = scopeFactory;
        this.session = session;
        PinInput.Foreground = new SolidColorBrush(Color.FromRgb(23, 32, 51));
        PinInput.CaretBrush = new SolidColorBrush(Color.FromRgb(23, 32, 51));
        PinInput.FontSize = 16;
        PinInput.PasswordChar = '●';
        Loaded += LoadUsers;
    }
    private async void LoadUsers(object sender, RoutedEventArgs e) { using var scope = scopeFactory.CreateScope(); UserSelector.ItemsSource = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().GetActiveUsersAsync(); UserSelector.SelectedIndex = 0; PinInput.Focus(); }
    private async void SignIn_Click(object sender, RoutedEventArgs e) => await SignInAsync();
    private async void PinInput_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) await SignInAsync(); }
    private async Task SignInAsync()
    {
        if (UserSelector.SelectedItem is not AppUser selected || string.IsNullOrWhiteSpace(PinInput.Password)) { ErrorText.Text = "Choose a staff account and enter its PIN."; return; }
        using var scope = scopeFactory.CreateScope(); var user = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().AuthenticateAsync(selected.Id, PinInput.Password);
        if (user is null) { ErrorText.Text = "That PIN is not correct."; PinInput.Clear(); PinInput.Focus(); return; }
        session.SignIn(user); DialogResult = true;
    }
}
