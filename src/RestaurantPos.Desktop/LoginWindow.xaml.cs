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
    private readonly StartupCoordinator startupCoordinator;
    private readonly LocalBackupScheduler backupScheduler;
    public LoginWindow(IServiceScopeFactory scopeFactory, UserSession session, StartupCoordinator startupCoordinator, LocalBackupScheduler backupScheduler)
    {
        InitializeComponent();
        this.scopeFactory = scopeFactory;
        this.session = session;
        this.startupCoordinator = startupCoordinator;
        this.backupScheduler = backupScheduler;
        PinInput.Foreground = new SolidColorBrush(Color.FromRgb(23, 32, 51));
        PinInput.CaretBrush = new SolidColorBrush(Color.FromRgb(23, 32, 51));
        PinInput.FontSize = 16;
        PinInput.PasswordChar = '●';
        Loaded += LoadUsers;
    }
    private async void LoadUsers(object sender, RoutedEventArgs e) => await PrepareLoginAsync();
    private async void RetryStartup_Click(object sender, RoutedEventArgs e) => await PrepareLoginAsync(true);
    private async Task PrepareLoginAsync(bool retry = false)
    {
        StartupProgressPanel.Visibility = Visibility.Visible;
        RetryStartupButton.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
        UserSelector.IsEnabled = PinInput.IsEnabled = SignInButton.IsEnabled = false;
        try
        {
            await startupCoordinator.InitializeAsync(retry);
            using var scope = scopeFactory.CreateScope();
            UserSelector.ItemsSource = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().GetActiveUsersAsync();
            UserSelector.SelectedIndex = 0;
            StartupProgressPanel.Visibility = Visibility.Collapsed;
            UserSelector.IsEnabled = PinInput.IsEnabled = SignInButton.IsEnabled = true;
            PinInput.Focus();
            backupScheduler.Start();
        }
        catch (Exception ex)
        {
            StartupProgressPanel.Visibility = Visibility.Collapsed;
            ErrorText.Text = $"Local data could not be prepared: {ex.Message}";
            RetryStartupButton.Visibility = Visibility.Visible;
        }
    }
    private async void SignIn_Click(object sender, RoutedEventArgs e) => await SignInAsync();
    private async void PinInput_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) await SignInAsync(); }
    private async Task SignInAsync()
    {
        if (!startupCoordinator.IsReady) { ErrorText.Text = "Please wait while local data is prepared."; return; }
        if (UserSelector.SelectedItem is not AppUser selected || string.IsNullOrWhiteSpace(PinInput.Password)) { ErrorText.Text = "Choose a staff account and enter its PIN."; return; }
        using var scope = scopeFactory.CreateScope(); var user = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().AuthenticateAsync(selected.Id, PinInput.Password);
        if (user is null) { ErrorText.Text = "That PIN is not correct."; PinInput.Clear(); PinInput.Focus(); return; }
        session.SignIn(user); DialogResult = true;
    }
}
