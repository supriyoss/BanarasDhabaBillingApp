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
    private readonly IAccessLicenseService accessLicenseService;
    public LoginWindow(IServiceScopeFactory scopeFactory, UserSession session, StartupCoordinator startupCoordinator, LocalBackupScheduler backupScheduler, IAccessLicenseService accessLicenseService)
    {
        InitializeComponent();
        this.scopeFactory = scopeFactory;
        this.session = session;
        this.startupCoordinator = startupCoordinator;
        this.backupScheduler = backupScheduler;
        this.accessLicenseService = accessLicenseService;
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
        LicenseStatusPanel.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
        UserSelector.IsEnabled = PinInput.IsEnabled = SignInButton.IsEnabled = false;
        try
        {
            await startupCoordinator.InitializeAsync(retry);
            var licenseStatus = await accessLicenseService.GetStatusAsync();
            ShowLicenseStatus(licenseStatus);
            StartupProgressPanel.Visibility = Visibility.Collapsed;
            if (!licenseStatus.IsActive) return;
            using var scope = scopeFactory.CreateScope();
            UserSelector.ItemsSource = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().GetActiveUsersAsync();
            UserSelector.SelectedIndex = 0;
            UserSelector.IsEnabled = PinInput.IsEnabled = SignInButton.IsEnabled = true;
            PinInput.Focus();
            backupScheduler.Start();
        }
        catch (Exception ex)
        {
            StartupProgressPanel.Visibility = Visibility.Collapsed;
            ErrorText.Text = $"Application access could not be prepared: {ex.Message}";
            RetryStartupButton.Visibility = Visibility.Visible;
        }
    }
    private void ShowLicenseStatus(AccessLicenseStatus status)
    {
        LicenseStatusPanel.Visibility = Visibility.Visible;
        LicenseStatusTitle.Text = status.IsActive ? "Access active" : "Access renewal required";
        LicenseStatusText.Text = status.Message;
        InstallationIdInput.Text = status.InstallationId;
        RenewalPanel.Visibility = !status.IsActive || status.DaysRemaining <= 7 ? Visibility.Visible : Visibility.Collapsed;
        LicenseStatusPanel.Background = new SolidColorBrush(status.IsActive ? Color.FromRgb(239, 246, 255) : Color.FromRgb(255, 247, 237));
        LicenseStatusPanel.BorderBrush = new SolidColorBrush(status.IsActive ? Color.FromRgb(191, 219, 254) : Color.FromRgb(253, 186, 116));
        LicenseStatusTitle.Foreground = new SolidColorBrush(status.IsActive ? Color.FromRgb(30, 58, 138) : Color.FromRgb(154, 52, 18));
    }
    private void CopyInstallationId_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InstallationIdInput.Text)) return;
        Clipboard.SetText(InstallationIdInput.Text);
        LicenseActionText.Text = "Installation ID copied.";
    }
    private async void ApplyRenewalCode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await accessLicenseService.ApplyRenewalCodeAsync(RenewalCodeInput.Text);
            LicenseActionText.Text = result.Message;
            ShowLicenseStatus(result.Status);
            if (result.Success) { RenewalCodeInput.Clear(); await PrepareLoginAsync(); }
        }
        catch (Exception ex) { LicenseActionText.Text = $"Access could not be renewed: {ex.Message}"; }
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
