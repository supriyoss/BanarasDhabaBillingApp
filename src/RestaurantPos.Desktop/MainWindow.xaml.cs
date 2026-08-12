using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application;
using RestaurantPos.Domain;
using RestaurantPos.Infrastructure;

namespace RestaurantPos.Desktop;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly UserSession session;
    private readonly LocalBackupScheduler backupScheduler;
    private Order? currentOrder;
    private bool choosingDineIn;
    private DiscountType selectedDiscountType = DiscountType.Percentage;
    private PaymentMethod selectedPaymentMethod = PaymentMethod.Cash;
    private SalesReportPeriod selectedReportPeriod = SalesReportPeriod.Daily;
    private bool invoicePrintedForCurrentOrder;
    private List<MenuItem> menuItems = [];
    public bool HasActiveOrder => currentOrder?.Status == OrderStatus.Open;
    public bool HasPaidOrder => currentOrder?.Status == OrderStatus.Paid;
    private bool IsAdministrator => session.CurrentUser?.Role == UserRole.Admin;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow(IServiceScopeFactory scopeFactory, UserSession session, LocalBackupScheduler backupScheduler)
    {
        InitializeComponent(); this.scopeFactory = scopeFactory; this.session = session; this.backupScheduler = backupScheduler; DataContext = this;
        GstRateInput.AcceptsReturn = false;
        GstRateInput.TextWrapping = TextWrapping.NoWrap;
        GstRateInput.Padding = new Thickness(0);
        GstRateInput.TextAlignment = TextAlignment.Center;
        GstRateInput.HorizontalContentAlignment = HorizontalAlignment.Center;
        GstRateInput.VerticalContentAlignment = VerticalAlignment.Center;
        GstRateInput.SetValue(System.Windows.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty, System.Windows.Controls.ScrollBarVisibility.Disabled);
        GstRateInput.SetValue(System.Windows.Controls.ScrollViewer.VerticalScrollBarVisibilityProperty, System.Windows.Controls.ScrollBarVisibility.Disabled);
        Loaded += LoadData;
    }

    private async void LoadData(object sender, RoutedEventArgs e)
    {
        using var scope = scopeFactory.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        menuItems = await db.MenuItems.Include(x => x.MenuCategory).Where(x => x.IsActive).OrderBy(x => x.MenuCategory!.SortOrder).ThenBy(x => x.SortOrder).ToListAsync();
        ApplyMenuFilter();
        TableSelector.ItemsSource = await db.DiningTables.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        SelectDiscountType(DiscountType.Percentage);
        SelectPaymentMethod(PaymentMethod.Cash);
        BusinessDate.SelectedDate = DateTime.Today;
        StaffText.Text = $"Current order - {session.CurrentUser!.DisplayName} ({session.CurrentUser.Role})";
        ServerNameInput.Text = session.CurrentUser.DisplayName;
        AdminNavButton.Visibility = IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        ReportsNavButton.Visibility = IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        MenuManagementNavButton.Visibility = IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        BackupNavButton.Visibility = IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        OfflineReadyIndicator.Visibility = IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        PosNavButton.Visibility = IsAdministrator ? Visibility.Collapsed : Visibility.Visible;
        HeldOrdersNavButton.Visibility = IsAdministrator ? Visibility.Collapsed : Visibility.Visible;
        HistoryFromDate.SelectedDate = DateTime.Today;
        if (IsAdministrator) { ShowScreen(AdminScreen); await LoadAdminDataAsync(); }
        else { ShowScreen(PosScreen); StatusText.Text = "Choose a table tile for dine-in, or start a takeaway order."; }
    }

    private void ShowPos_Click(object sender, RoutedEventArgs e) { if (!IsAdministrator) ShowScreen(PosScreen); }
    private async void ShowHeldOrders_Click(object sender, RoutedEventArgs e) { if (IsAdministrator) return; ShowScreen(HeldOrdersScreen); await LoadHeldOrdersAsync(); }
    private async void ShowReports_Click(object sender, RoutedEventArgs e) { if (!IsAdministrator) return; ShowScreen(ReportsScreen); await LoadReportAsync(); }
    private async void ShowMenuManagement_Click(object sender, RoutedEventArgs e) { if (!IsAdministrator) return; ShowScreen(MenuManagementScreen); await LoadMenuManagementAsync(); }
    private async void ShowAdmin_Click(object sender, RoutedEventArgs e) { if (session.CurrentUser?.Role != UserRole.Admin) return; ShowScreen(AdminScreen); await LoadAdminDataAsync(); }
    private void ShowScreen(FrameworkElement screen)
    {
        PosScreen.Visibility = screen == PosScreen ? Visibility.Visible : Visibility.Collapsed; HeldOrdersScreen.Visibility = screen == HeldOrdersScreen ? Visibility.Visible : Visibility.Collapsed; ReportsScreen.Visibility = screen == ReportsScreen ? Visibility.Visible : Visibility.Collapsed; MenuManagementScreen.Visibility = screen == MenuManagementScreen ? Visibility.Visible : Visibility.Collapsed; AdminScreen.Visibility = screen == AdminScreen ? Visibility.Visible : Visibility.Collapsed;
        SetActiveNavigation(PosNavButton, screen == PosScreen); SetActiveNavigation(HeldOrdersNavButton, screen == HeldOrdersScreen); SetActiveNavigation(ReportsNavButton, screen == ReportsScreen); SetActiveNavigation(MenuManagementNavButton, screen == MenuManagementScreen); SetActiveNavigation(AdminNavButton, screen == AdminScreen);
    }
    private static void SetActiveNavigation(System.Windows.Controls.Button button, bool isActive)
    {
        button.Background = isActive ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : Brushes.Transparent;
        button.Foreground = isActive ? Brushes.White : new SolidColorBrush(Color.FromRgb(203, 213, 225));
    }
    private void Logout_Click(object sender, RoutedEventArgs e) => (System.Windows.Application.Current as App)?.Logout();
    private async void BackupNow_Click(object sender, RoutedEventArgs e) { if (!IsAdministrator) return; try { await backupScheduler.CreateNowAsync(); AdminStatusText.Text = "Local backup created."; } catch (Exception ex) { AdminStatusText.Text = ex.Message; } }

    private void BeginDineIn_Click(object sender, RoutedEventArgs e)
    {
        choosingDineIn = true;
        TableSelectionPanel.Visibility = Visibility.Visible;
        DineInButton.Style = (Style)FindResource("PrimaryButton");
        TakeawayButton.Style = (Style)FindResource("CompactAction");
        StatusText.Text = "Choose a table to begin the dine-in order.";
    }
    private async void TableSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!choosingDineIn || TableSelector.SelectedItem is not DiningTable table) return;
        choosingDineIn = false;
        await CreateOrderAsync(OrderType.DineIn, table.Id);
    }
    private async void TakeawayOrder_Click(object sender, RoutedEventArgs e)
    {
        choosingDineIn = false;
        TableSelector.SelectedItem = null;
        TableSelectionPanel.Visibility = Visibility.Collapsed;
        DineInButton.Style = (Style)FindResource("CompactAction");
        TakeawayButton.Style = (Style)FindResource("PrimaryButton");
        await CreateOrderAsync(OrderType.Takeaway, null);
    }
    private async Task CreateOrderAsync(OrderType type, int? tableId)
    {
        if (IsAdministrator) { ShowScreen(AdminScreen); AdminStatusText.Text = "Administrator accounts cannot create orders."; return; }
        try { using var scope = scopeFactory.CreateScope(); currentOrder = await scope.ServiceProvider.GetRequiredService<IOrderWorkflow>().CreateAsync(type, tableId, session.CurrentUser!.Id, ServerNameInput.Text); invoicePrintedForCurrentOrder = false; ShowScreen(PosScreen); RefreshOrder("Order created. Add menu items."); }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task LoadHeldOrdersAsync()
    {
        try { using var scope = scopeFactory.CreateScope(); HeldOrdersGrid.ItemsSource = await scope.ServiceProvider.GetRequiredService<IOrderWorkflow>().GetHeldOrdersAsync(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void RefreshHeldOrders_Click(object sender, RoutedEventArgs e) => await LoadHeldOrdersAsync();
    private async void ResumeHeldOrder_Click(object sender, RoutedEventArgs e) => await ResumeSelectedHeldOrderAsync();
    private async void HeldOrdersGrid_DoubleClick(object sender, MouseButtonEventArgs e) => await ResumeSelectedHeldOrderAsync();
    private async Task ResumeSelectedHeldOrderAsync()
    {
        if (HeldOrdersGrid.SelectedItem is not Order order) return;
        try { using var scope = scopeFactory.CreateScope(); currentOrder = await scope.ServiceProvider.GetRequiredService<IOrderWorkflow>().ResumeAsync(order.Id, session.CurrentUser!.Id); invoicePrintedForCurrentOrder = false; ShowScreen(PosScreen); RefreshOrder("Held order resumed."); }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void RefreshReport_Click(object sender, RoutedEventArgs e) => await LoadReportAsync();
    private async void DailyReport_Click(object sender, RoutedEventArgs e) { selectedReportPeriod = SalesReportPeriod.Daily; await LoadReportAsync(); }
    private async void MonthlyReport_Click(object sender, RoutedEventArgs e) { selectedReportPeriod = SalesReportPeriod.Monthly; await LoadReportAsync(); }
    private async void YearlyReport_Click(object sender, RoutedEventArgs e) { selectedReportPeriod = SalesReportPeriod.Yearly; await LoadReportAsync(); }
    private async void BusinessDate_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (IsLoaded && ReportsScreen.Visibility == Visibility.Visible) await LoadReportAsync(); }
    private async Task LoadReportAsync()
    {
        try
        {
            using var scope = scopeFactory.CreateScope(); var report = await scope.ServiceProvider.GetRequiredService<IReportingService>().GetSalesAsync(BusinessDate.SelectedDate ?? DateTime.Today, selectedReportPeriod);
            OrderCountText.Text = $"Paid bills: {report.PaidOrderCount}"; SalesTotalText.Text = $"Sales: {report.SalesTotal:N2}"; TaxTotalText.Text = $"GST: {report.TaxTotal:N2}";
            ReportTitleText.Text = selectedReportPeriod switch { SalesReportPeriod.Monthly => "Monthly sales report", SalesReportPeriod.Yearly => "Yearly sales report", _ => "Daily sales report" };
            ReportDescriptionText.Text = $"Sales, GST, payments, and activity for {FormatReportRange(report)}.";
            PaymentsGrid.ItemsSource = report.Payments; AuditGrid.ItemsSource = report.Activity; SetReportPeriodButtons();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private static string FormatReportRange(SalesReport report) => report.Period switch
    {
        SalesReportPeriod.Monthly => report.PeriodStart.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
        SalesReportPeriod.Yearly => report.PeriodStart.ToString("yyyy", CultureInfo.CurrentCulture),
        _ => report.PeriodStart.ToString("dd MMMM yyyy", CultureInfo.CurrentCulture)
    };
    private void SetReportPeriodButtons()
    {
        DailyReportButton.Style = (Style)FindResource(selectedReportPeriod == SalesReportPeriod.Daily ? "PrimaryButton" : "CompactAction");
        MonthlyReportButton.Style = (Style)FindResource(selectedReportPeriod == SalesReportPeriod.Monthly ? "PrimaryButton" : "CompactAction");
        YearlyReportButton.Style = (Style)FindResource(selectedReportPeriod == SalesReportPeriod.Yearly ? "PrimaryButton" : "CompactAction");
    }

    private async Task LoadAdminDataAsync()
    {
        try
        {
            using var scope = scopeFactory.CreateScope(); var admin = scope.ServiceProvider.GetRequiredService<IAdministrationService>();
            StaffGrid.ItemsSource = await admin.GetStaffAsync();
            NewMenuCategorySelector.ItemsSource = await admin.GetCategoriesAsync();
            GstRateInput.Text = (await admin.GetSettingsAsync()).GstRate.ToString("0.##", CultureInfo.CurrentCulture);
            NewStaffRoleSelector.ItemsSource = new[] { UserRole.Manager, UserRole.Cashier, UserRole.Server };
            if (NewStaffRoleSelector.SelectedIndex < 0) NewStaffRoleSelector.SelectedIndex = 0;
            if (NewMenuCategorySelector.SelectedIndex < 0) NewMenuCategorySelector.SelectedIndex = 0;
            await LoadHistoryAsync(admin);
        }
        catch (Exception ex) { AdminStatusText.Text = ex.Message; }
    }
    private async void AddStaff_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (NewStaffRoleSelector.SelectedItem is not UserRole role) throw new InvalidOperationException("Select a staff role.");
            using var scope = scopeFactory.CreateScope(); await scope.ServiceProvider.GetRequiredService<IAdministrationService>().AddStaffAsync(NewStaffNameInput.Text, NewStaffPinInput.Password, role, session.CurrentUser!.Id);
            NewStaffNameInput.Clear(); NewStaffPinInput.Clear(); AdminStatusText.Text = "Staff account created."; await LoadAdminDataAsync();
        }
        catch (Exception ex) { AdminStatusText.Text = ex.Message; }
    }
    private async void ChangePin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope(); await scope.ServiceProvider.GetRequiredService<IAdministrationService>().ChangePinAsync(session.CurrentUser!.Id, CurrentPinInput.Password, NewPinInput.Password);
            CurrentPinInput.Clear(); NewPinInput.Clear(); AdminStatusText.Text = "Your PIN was updated.";
        }
        catch (Exception ex) { AdminStatusText.Text = ex.Message; }
    }
    private async void ResetSelectedStaffPin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (StaffGrid.SelectedItems.Count != 1 || StaffGrid.SelectedItem is not AppUser user) throw new InvalidOperationException("Select one staff account to reset its PIN.");
            using var scope = scopeFactory.CreateScope(); await scope.ServiceProvider.GetRequiredService<IAdministrationService>().ResetStaffPinAsync(user.Id, ResetStaffPinInput.Password, session.CurrentUser!.Id);
            ResetStaffPinInput.Clear(); AdminStatusText.Text = $"PIN reset for {user.DisplayName}.";
        }
        catch (Exception ex) { AdminStatusText.Text = ex.Message; }
    }
    private async void AddMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (NewMenuCategorySelector.SelectedItem is not MenuCategory category || !decimal.TryParse(NewMenuPriceInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var price)) throw new InvalidOperationException("Choose a category and enter a valid price.");
            using var scope = scopeFactory.CreateScope(); await scope.ServiceProvider.GetRequiredService<IAdministrationService>().AddMenuItemAsync(category.Id, NewMenuItemNameInput.Text, price, session.CurrentUser!.Id);
            NewMenuItemNameInput.Clear(); NewMenuPriceInput.Clear(); MenuManagementStatusText.Text = "Menu item added."; await ReloadMenuAsync(); await LoadMenuManagementAsync();
        }
        catch (Exception ex) { AdminStatusText.Text = ex.Message; }
    }
    private async Task ReloadMenuAsync()
    {
        using var scope = scopeFactory.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        menuItems = await db.MenuItems.Include(x => x.MenuCategory).Where(x => x.IsActive).OrderBy(x => x.MenuCategory!.SortOrder).ThenBy(x => x.SortOrder).ToListAsync();
        ApplyMenuFilter();
    }
    private async Task LoadMenuManagementAsync()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var admin = scope.ServiceProvider.GetRequiredService<IAdministrationService>();
            AdminMenuGrid.ItemsSource = await admin.GetActiveMenuItemsAsync();
            NewMenuCategorySelector.ItemsSource = await admin.GetCategoriesAsync();
            if (NewMenuCategorySelector.SelectedIndex < 0) NewMenuCategorySelector.SelectedIndex = 0;
        }
        catch (Exception ex) { MenuManagementStatusText.Text = ex.Message; }
    }
    private async void RefreshMenuManagement_Click(object sender, RoutedEventArgs e) => await LoadMenuManagementAsync();
    private async void DeactivateMenuItems_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ids = AdminMenuGrid.SelectedItems.OfType<MenuItem>().Select(x => x.Id).ToArray();
            using var scope = scopeFactory.CreateScope();
            var count = await scope.ServiceProvider.GetRequiredService<IAdministrationService>().DeactivateMenuItemsAsync(ids, session.CurrentUser!.Id);
            AdminStatusText.Text = $"{count} menu item(s) removed from the active menu.";
            MenuManagementStatusText.Text = $"{count} menu item(s) removed. Historical sales are unchanged.";
            await ReloadMenuAsync(); await LoadMenuManagementAsync();
        }
        catch (Exception ex) { MenuManagementStatusText.Text = ex.Message; }
    }
    private async void DeactivateStaffAccounts_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ids = StaffGrid.SelectedItems.OfType<AppUser>().Select(x => x.Id).ToArray();
            using var scope = scopeFactory.CreateScope();
            var count = await scope.ServiceProvider.GetRequiredService<IAdministrationService>().DeactivateStaffAccountsAsync(ids, session.CurrentUser!.Id);
            AdminStatusText.Text = $"{count} staff account(s) deactivated.";
            await LoadAdminDataAsync();
        }
        catch (Exception ex) { AdminStatusText.Text = ex.Message; }
    }
    private async void UpdateGstRate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!decimal.TryParse(GstRateInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var rate)) throw new InvalidOperationException("Enter a valid GST rate.");
            using var scope = scopeFactory.CreateScope(); await scope.ServiceProvider.GetRequiredService<IAdministrationService>().UpdateGstRateAsync(rate, session.CurrentUser!.Id);
            AdminStatusText.Text = "Bill GST rate updated for newly created orders.";
        }
        catch (Exception ex) { AdminStatusText.Text = ex.Message; }
    }
    private async void LoadHistory_Click(object sender, RoutedEventArgs e)
    {
        try { using var scope = scopeFactory.CreateScope(); await LoadHistoryAsync(scope.ServiceProvider.GetRequiredService<IAdministrationService>()); }
        catch (Exception ex) { AdminStatusText.Text = ex.Message; }
    }
    private async Task LoadHistoryAsync(IAdministrationService admin) => HistoryGrid.ItemsSource = await admin.GetOrderHistoryAsync(HistoryFromDate.SelectedDate ?? DateTime.Today);

    private async void AddItem_Click(object sender, RoutedEventArgs e) => await AddSelectedItemAsync();
    private async void MenuGrid_DoubleClick(object sender, MouseButtonEventArgs e) => await AddSelectedItemAsync();
    private async void AddMenuItemToOrder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is int menuItemId && menuItems.FirstOrDefault(x => x.Id == menuItemId) is MenuItem item) await AddMenuItemToOrderAsync(item);
    }
    private async Task AddSelectedItemAsync()
    {
        var item = MenuGrid.CurrentItem as MenuItem ?? MenuGrid.SelectedItem as MenuItem;
        if (item is not null) await AddMenuItemToOrderAsync(item);
    }
    private async Task AddMenuItemToOrderAsync(MenuItem item)
    {
        if (currentOrder is null) { StatusText.Text = "Start a bill before adding menu items."; return; }
        await ApplyAsync(w => w.AddMenuItemAsync(currentOrder.Id, item.Id, session.CurrentUser!.Id), $"Added {item.Name}.");
    }
    private void MenuSearchInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyMenuFilter();
    private void ApplyMenuFilter()
    {
        var query = MenuSearchInput?.Text.Trim() ?? string.Empty;
        MenuGrid.ItemsSource = string.IsNullOrWhiteSpace(query) ? menuItems : menuItems.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || x.MenuCategory?.Name.Contains(query, StringComparison.OrdinalIgnoreCase) == true).ToList();
    }
    private async void IncreaseLineQuantity_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OrderLine line && currentOrder is not null) await ApplyAsync(w => w.ChangeQuantityAsync(currentOrder.Id, line.Id, line.Quantity + 1, session.CurrentUser!.Id), "Quantity updated.");
    }
    private async void DecreaseLineQuantity_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OrderLine line && currentOrder is not null) await ApplyAsync(w => w.ChangeQuantityAsync(currentOrder.Id, line.Id, line.Quantity - 1, session.CurrentUser!.Id), "Quantity updated.");
    }
    private async void ApplyBillDiscount_Click(object sender, RoutedEventArgs e)
    {
        if (currentOrder is null) return;
        if (!decimal.TryParse(BillDiscountValueInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) || value < 0 || (selectedDiscountType == DiscountType.Percentage && value > 100)) { StatusText.Text = "Enter a valid bill discount value."; return; }
        await ApplyAsync(w => w.SetOrderDiscountAsync(currentOrder.Id, selectedDiscountType, value, session.CurrentUser!.Id), "Bill discount saved.");
    }
    private void PercentageDiscount_Click(object sender, RoutedEventArgs e) => SelectDiscountType(DiscountType.Percentage);
    private void FixedDiscount_Click(object sender, RoutedEventArgs e) => SelectDiscountType(DiscountType.FixedAmount);
    private void SelectDiscountType(DiscountType type)
    {
        selectedDiscountType = type;
        PercentageDiscountButton.Style = (Style)FindResource(type == DiscountType.Percentage ? "PrimaryButton" : "CompactAction");
        FixedDiscountButton.Style = (Style)FindResource(type == DiscountType.FixedAmount ? "PrimaryButton" : "CompactAction");
    }
    private async void Hold_Click(object sender, RoutedEventArgs e)
    {
        if (currentOrder is null || currentOrder.Status != OrderStatus.Open) return;
        await ApplyAsync(w => w.HoldAsync(currentOrder.Id, session.CurrentUser!.Id), "Order held.");
        if (currentOrder?.Status == OrderStatus.Held) { ShowScreen(HeldOrdersScreen); await LoadHeldOrdersAsync(); }
    }
    private async void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (currentOrder is null) return;
        await ApplyAsync(w => w.TakePaymentAsync(currentOrder.Id, selectedPaymentMethod, currentOrder.GrandTotal, session.CurrentUser!.Id), "Payment recorded.");
        if (currentOrder?.Status == OrderStatus.Paid) await PrintReceiptAsync(false);
    }
    private void CashPayment_Click(object sender, RoutedEventArgs e) => SelectPaymentMethod(PaymentMethod.Cash);
    private void CardPayment_Click(object sender, RoutedEventArgs e) => SelectPaymentMethod(PaymentMethod.Card);
    private void UpiPayment_Click(object sender, RoutedEventArgs e) => SelectPaymentMethod(PaymentMethod.Upi);
    private void SelectPaymentMethod(PaymentMethod method)
    {
        selectedPaymentMethod = method;
        CashPaymentButton.Style = (Style)FindResource(method == PaymentMethod.Cash ? "PrimaryButton" : "CompactAction");
        CardPaymentButton.Style = (Style)FindResource(method == PaymentMethod.Card ? "PrimaryButton" : "CompactAction");
        UpiPaymentButton.Style = (Style)FindResource(method == PaymentMethod.Upi ? "PrimaryButton" : "CompactAction");
    }
    private async void Reprint_Click(object sender, RoutedEventArgs e) => await PrintReceiptAsync(true);
    private async Task PrintReceiptAsync(bool isReprint)
    {
        if (currentOrder is null) return;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var printed = await scope.ServiceProvider.GetRequiredService<IReceiptPrinter>().PrintAsync(currentOrder, isReprint);
            if (!printed) { StatusText.Text = "Printing was cancelled."; return; }
            invoicePrintedForCurrentOrder = true;
            ReprintButton.Visibility = Visibility.Visible;
            StatusText.Text = isReprint ? "Invoice sent for reprint." : "Invoice sent to the printer.";
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async Task ApplyAsync(Func<IOrderWorkflow, Task<Order>> action, string message) { try { using var scope = scopeFactory.CreateScope(); currentOrder = await action(scope.ServiceProvider.GetRequiredService<IOrderWorkflow>()); RefreshOrder(message); } catch (Exception ex) { ShowError(ex); } }
    private void RefreshOrder(string message)
    {
        CartGrid.ItemsSource = currentOrder?.Lines.ToList();
        OrderInfoText.Text = currentOrder is null ? "No active order" : $"{currentOrder.InvoiceNumber} - {currentOrder.Type} - Server: {currentOrder.ServerName} - {currentOrder.Status}";
        BillDiscountTotalText.Text = currentOrder is null ? string.Empty : currentOrder.DiscountAmount.ToString("N2", CultureInfo.CurrentCulture);
        GstTotalText.Text = currentOrder is null ? string.Empty : currentOrder.TaxAmount.ToString("N2", CultureInfo.CurrentCulture);
        GrandTotalText.Text = currentOrder is null ? string.Empty : currentOrder.GrandTotal.ToString("N2", CultureInfo.CurrentCulture);
        ReprintButton.Visibility = currentOrder?.Status == OrderStatus.Paid && invoicePrintedForCurrentOrder ? Visibility.Visible : Visibility.Collapsed;
        if (currentOrder is not null) { SelectDiscountType(currentOrder.DiscountType == DiscountType.None ? DiscountType.Percentage : currentOrder.DiscountType); BillDiscountValueInput.Text = currentOrder.DiscountValue.ToString("0.##", CultureInfo.CurrentCulture); }
        StatusText.Text = message; OnPropertyChanged(nameof(HasActiveOrder)); OnPropertyChanged(nameof(HasPaidOrder));
    }
    private void ShowError(Exception ex) { ShowScreen(PosScreen); StatusText.Text = ex.Message; }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
