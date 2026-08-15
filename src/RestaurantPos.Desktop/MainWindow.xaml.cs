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
    private OrderType? pendingOrderType;
    private int? pendingTableId;
    private string? selectedTableLabel;
    private readonly System.Windows.Controls.ComboBox menuCategoryFilter = new() { Width = 210, Margin = new Thickness(8, 0, 0, 0), DisplayMemberPath = "Name" };
    private readonly System.Windows.Controls.ComboBox managementCategorySelector = new() { Height = 40, Margin = new Thickness(0, 4, 10, 0), DisplayMemberPath = "Name", MaxDropDownHeight = 260 };
    private readonly System.Windows.Controls.ComboBox staffRoleSelector = new() { Margin = new Thickness(4), MinHeight = 34 };
    private readonly System.Windows.Controls.PasswordBox confirmStaffPinInput = new() { Margin = new Thickness(4), Height = 34, Padding = new Thickness(9, 6, 9, 6) };
    private readonly System.Windows.Controls.DataGrid applicationAccountGrid = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = System.Windows.Controls.DataGridSelectionMode.Single, Height = 170, Margin = new Thickness(0, 10, 0, 8) };
    private readonly System.Windows.Controls.PasswordBox applicationResetPinInput = new() { Width = 150, Margin = new Thickness(8, 0, 8, 0) };
    private readonly System.Windows.Controls.Grid diningScreen = new() { Visibility = Visibility.Collapsed };
    private readonly System.Windows.Controls.Grid floorPlanEditorScreen = new() { Visibility = Visibility.Collapsed };
    private FloorPlanEditorView? floorPlanEditorView;
    private readonly System.Windows.Controls.ComboBox diningLayoutSelector = new() { Width = 190, DisplayMemberPath = "Name", Margin = new Thickness(4) };
    private readonly System.Windows.Controls.Grid diningFloorGrid = new() { Background = Brushes.White, MinHeight = 520, MinWidth = 850 };
    private readonly System.Windows.Controls.Button diningNavButton = new() { Content = "Dining" };
    private IReadOnlyList<FloorPlanView> diningLayouts = [];
    private readonly System.Windows.Controls.Button preparationModeButton = new() { Content = "Mark selected as Packed" };
    private readonly System.Windows.Controls.Button holdTakeawayButton = new() { Content = "Save open takeaway", Visibility = Visibility.Collapsed };
    private readonly System.Windows.Controls.Button updateServerNameButton = new() { Content = "Update server", Visibility = Visibility.Collapsed };
    private readonly System.Windows.Controls.TextBlock orderContextBanner = new() { FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(30, 58, 138)), Background = new SolidColorBrush(Color.FromRgb(219, 234, 254)), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 9) };
    private readonly System.Windows.Controls.ComboBox editMenuCategorySelector = new() { MinWidth = 165, DisplayMemberPath = "Name", Margin = new Thickness(4) };
    private readonly System.Windows.Controls.TextBox editMenuNameInput = new() { MinWidth = 190, Margin = new Thickness(4) };
    private readonly System.Windows.Controls.TextBox editMenuPriceInput = new() { Width = 110, Margin = new Thickness(4), TextAlignment = TextAlignment.Right };
    private System.Windows.Controls.Border? menuEditorCard;
    private bool choosingDineIn;
    private DiscountType selectedDiscountType = DiscountType.Percentage;
    private PaymentMethod selectedPaymentMethod = PaymentMethod.Cash;
    private SalesReportPeriod selectedReportPeriod = SalesReportPeriod.Daily;
    private bool invoicePrintedForCurrentOrder;
    private List<MenuItem> menuItems = [];
    public bool HasActiveOrder => currentOrder?.Status == OrderStatus.Open || pendingOrderType is not null;
    public bool HasPaidOrder => currentOrder?.Status == OrderStatus.Paid;
    private bool IsAdministrator => session.CurrentUser?.Role == UserRole.Admin;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow(IServiceScopeFactory scopeFactory, UserSession session, LocalBackupScheduler backupScheduler)
    {
        InitializeComponent(); this.scopeFactory = scopeFactory; this.session = session; this.backupScheduler = backupScheduler; DataContext = this;
        MenuGrid.MouseDoubleClick += MenuGrid_DoubleClick;
        if (TableSelectionPanel.Parent is FrameworkElement legacyOrderStartPanel) legacyOrderStartPanel.Visibility = Visibility.Collapsed;
        HeldOrdersNavButton.Content = "Open takeaways";
        UpdateTakeawayQueueLabels();
        if (PosNavButton.Parent is System.Windows.Controls.Panel primaryNavigation) primaryNavigation.Children.Remove(PosNavButton);
        if (LeaveOrderButton.Parent is System.Windows.Controls.Panel orderActions)
        {
            holdTakeawayButton.Click += HoldTakeaway_Click; orderActions.Children.Add(holdTakeawayButton);
            preparationModeButton.Click += TogglePacked_Click; orderActions.Children.Add(preparationModeButton);
            var cancel = new System.Windows.Controls.Button { Content = "Cancel order" }; cancel.Click += CancelOrder_Click; orderActions.Children.Add(cancel);
        }
        if (StaffText.Parent is System.Windows.Controls.StackPanel orderHeader) orderHeader.Children.Insert(0, orderContextBanner);
        BuildDiningScreen();
        BuildFloorPlanEditorScreen();
        ConfigureRoleAccountForm();
        ConfigureOperationalGrids();
        ConfigureNumericColumnAlignment();
        ConfigureMenuEditor();
        ConfigureApplicationAccountManagement();
        ConfigureServerNameEditor();
        CartGrid.SelectionChanged += (_, _) => UpdatePreparationAction();
        menuCategoryFilter.SelectionChanged += (_, _) => ApplyMenuFilter();
        if (MenuSearchInput.Parent is System.Windows.Controls.Panel menuHeader)
        {
            var filterBar = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            filterBar.Children.Add(new System.Windows.Controls.TextBlock { Text = "Category", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            filterBar.Children.Add(menuCategoryFilter);
            menuHeader.Children.Add(filterBar);
        }
        if (NewMenuCategorySelector.Parent is System.Windows.Controls.Panel categoryHost) { NewMenuCategorySelector.Visibility = Visibility.Collapsed; categoryHost.Children.Add(managementCategorySelector); }
        NewMenuItemNameInput.Height = 40;
        NewMenuPriceInput.Height = 40;
        NewMenuPriceInput.ToolTip = "Menu item price must be greater than 0";
        if (NewMenuPriceInput.Parent is System.Windows.Controls.StackPanel pricePanel) pricePanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Price must be greater than 0", Foreground = new SolidColorBrush(Color.FromRgb(146, 64, 14)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 4, 6, 0) });
        foreach (var column in HeldOrdersGrid.Columns.Where(x => Equals(x.Header, "Opened")).OfType<System.Windows.Controls.DataGridTextColumn>()) column.Binding = new System.Windows.Data.Binding(nameof(Order.OpenedLocal)) { StringFormat = "dd MMM HH:mm" };
        foreach (var column in HistoryGrid.Columns.Where(x => Equals(x.Header, "Closed")).OfType<System.Windows.Controls.DataGridTextColumn>()) column.Binding = new System.Windows.Data.Binding(nameof(Order.ClosedLocal)) { StringFormat = "dd MMM yyyy HH:mm" };
        GstRateInput.AcceptsReturn = false;
        GstRateInput.TextWrapping = TextWrapping.NoWrap;
        GstRateInput.Padding = new Thickness(0);
        GstRateInput.TextAlignment = TextAlignment.Center;
        GstRateInput.HorizontalContentAlignment = HorizontalAlignment.Center;
        GstRateInput.VerticalContentAlignment = VerticalAlignment.Center;
        GstRateInput.SetValue(System.Windows.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty, System.Windows.Controls.ScrollBarVisibility.Disabled);
        GstRateInput.SetValue(System.Windows.Controls.ScrollViewer.VerticalScrollBarVisibilityProperty, System.Windows.Controls.ScrollBarVisibility.Disabled);
        var gstUpdateButton = LogicalDescendants(AdminScreen).OfType<System.Windows.Controls.Button>().FirstOrDefault(x => Equals(x.Content, "Update"));
        if (gstUpdateButton is not null)
        {
            gstUpdateButton.Content = "Update GST";
            gstUpdateButton.Style = (Style)FindResource("PrimaryButton");
            gstUpdateButton.Padding = new Thickness(14, 8, 14, 8);
        }
        Loaded += LoadData;
    }

    private async void LoadData(object sender, RoutedEventArgs e)
    {
        using var scope = scopeFactory.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        menuItems = await db.MenuItems.Include(x => x.MenuCategory).Where(x => x.IsActive).OrderBy(x => x.MenuCategory!.SortOrder).ThenBy(x => x.SortOrder).ToListAsync();
        ApplyMenuFilter();
        menuCategoryFilter.ItemsSource = new[] { new MenuCategory { Id = 0, Name = "All categories" } }.Concat(menuItems.Select(x => x.MenuCategory!).DistinctBy(x => x.Id)).ToList(); menuCategoryFilter.SelectedIndex = 0;
        TableSelector.ItemsSource = await db.DiningTables.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        SelectDiscountType(DiscountType.Percentage);
        SelectPaymentMethod(PaymentMethod.Cash);
        BusinessDate.SelectedDate = DateTime.Today;
        StaffText.Text = $"Current order - {session.CurrentUser!.DisplayName} ({session.CurrentUser.Role})";
        HomeStaffText.Text = $"Signed in as {session.CurrentUser.DisplayName} ({session.CurrentUser.Role})";
        ServerNameInput.Clear();
        var isRestaurantManager = RolePermissions.CanManageRestaurant(session.CurrentUser.Role);
        AdminNavButton.Visibility = IsAdministrator || isRestaurantManager ? Visibility.Visible : Visibility.Collapsed;
        AdminNavButton.Content = IsAdministrator ? "Application" : "Restaurant";
        ReportsNavButton.Visibility = isRestaurantManager ? Visibility.Visible : Visibility.Collapsed;
        MenuManagementNavButton.Visibility = isRestaurantManager ? Visibility.Visible : Visibility.Collapsed;
        BackupNavButton.Visibility = Visibility.Collapsed;
        OfflineReadyIndicator.Visibility = IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        PosNavButton.Visibility = Visibility.Collapsed;
        HomeNavButton.Visibility = IsAdministrator ? Visibility.Collapsed : Visibility.Visible;
        diningNavButton.Visibility = IsAdministrator ? Visibility.Collapsed : Visibility.Visible;
        FloorPlanEditorButton.Visibility = session.CurrentUser.Role == UserRole.Manager ? Visibility.Visible : Visibility.Collapsed;
        HeldOrdersNavButton.Visibility = IsAdministrator ? Visibility.Collapsed : Visibility.Visible;
        ReorderManagerNavigation();
        HistoryFromDate.SelectedDate = DateTime.Today;
        if (IsAdministrator) { ShowScreen(ApplicationMaintenanceScreen); await LoadApplicationAccountsAsync(); }
        else { ShowScreen(HomeScreen); StatusText.Text = "Choose Dining or Takeaway from Home."; }
    }

    private void ShowHome_Click(object sender, RoutedEventArgs e) { if (!IsAdministrator) ShowScreen(HomeScreen); }
    private void ShowPos_Click(object sender, RoutedEventArgs e) { if (!IsAdministrator) ShowScreen(PosScreen); }
    private async void ShowHeldOrders_Click(object sender, RoutedEventArgs e) { if (IsAdministrator) return; ShowScreen(HeldOrdersScreen); await LoadHeldOrdersAsync(); }
    private async void ShowReports_Click(object sender, RoutedEventArgs e) { if (session.CurrentUser?.Role != UserRole.Manager) return; ShowScreen(ReportsScreen); await LoadReportAsync(); }
    private async void ShowMenuManagement_Click(object sender, RoutedEventArgs e) { if (session.CurrentUser?.Role != UserRole.Manager) return; ShowScreen(MenuManagementScreen); await LoadMenuManagementAsync(); }
    private async void ShowAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (IsAdministrator) { ShowScreen(ApplicationMaintenanceScreen); await LoadApplicationAccountsAsync(); return; }
        if (session.CurrentUser?.Role == UserRole.Manager) { ShowScreen(AdminScreen); await LoadAdminDataAsync(); }
    }
    private void ShowScreen(FrameworkElement screen)
    {
        HomeScreen.Visibility = screen == HomeScreen ? Visibility.Visible : Visibility.Collapsed; PosScreen.Visibility = screen == PosScreen ? Visibility.Visible : Visibility.Collapsed; HeldOrdersScreen.Visibility = screen == HeldOrdersScreen ? Visibility.Visible : Visibility.Collapsed; ReportsScreen.Visibility = screen == ReportsScreen ? Visibility.Visible : Visibility.Collapsed; MenuManagementScreen.Visibility = screen == MenuManagementScreen ? Visibility.Visible : Visibility.Collapsed; AdminScreen.Visibility = screen == AdminScreen ? Visibility.Visible : Visibility.Collapsed; ApplicationMaintenanceScreen.Visibility = screen == ApplicationMaintenanceScreen ? Visibility.Visible : Visibility.Collapsed;
        diningScreen.Visibility = screen == diningScreen ? Visibility.Visible : Visibility.Collapsed;
        floorPlanEditorScreen.Visibility = screen == floorPlanEditorScreen ? Visibility.Visible : Visibility.Collapsed;
        SetActiveNavigation(HomeNavButton, screen == HomeScreen); SetActiveNavigation(PosNavButton, screen == PosScreen); SetActiveNavigation(HeldOrdersNavButton, screen == HeldOrdersScreen); SetActiveNavigation(ReportsNavButton, screen == ReportsScreen); SetActiveNavigation(MenuManagementNavButton, screen == MenuManagementScreen); SetActiveNavigation(AdminNavButton, screen == AdminScreen || screen == ApplicationMaintenanceScreen);
        SetActiveNavigation(diningNavButton, screen == diningScreen);
    }

    private void BuildDiningScreen()
    {
        if (HomeScreen.Parent is not System.Windows.Controls.Grid host) return;
        diningNavButton.Visibility = IsAdministrator ? Visibility.Collapsed : Visibility.Visible;
        diningNavButton.Style = (Style)FindResource("NavButton"); diningNavButton.Click += async (_, _) => await OpenDiningFloorAsync();
        if (HomeNavButton.Parent is System.Windows.Controls.StackPanel nav) nav.Children.Insert(Math.Min(1, nav.Children.Count), diningNavButton);
        diningScreen.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        diningScreen.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
        var header = new System.Windows.Controls.DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var title = new System.Windows.Controls.StackPanel();
        title.Children.Add(new System.Windows.Controls.TextBlock { Text = "Dining floor plan", FontSize = 28, FontWeight = FontWeights.SemiBold });
        title.Children.Add(new System.Windows.Controls.TextBlock { Text = "Select a table to start or reopen its order.", Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)) });
        header.Children.Add(title);
        var refresh = new System.Windows.Controls.Button { Content = "Refresh" }; refresh.Click += async (_, _) => await LoadDiningFloorAsync();
        System.Windows.Controls.DockPanel.SetDock(refresh, System.Windows.Controls.Dock.Right); header.Children.Add(refresh);
        System.Windows.Controls.DockPanel.SetDock(diningLayoutSelector, System.Windows.Controls.Dock.Right); header.Children.Add(diningLayoutSelector);
        diningScreen.Children.Add(header);
        var scroll = new System.Windows.Controls.ScrollViewer { Content = diningFloorGrid, HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
        System.Windows.Controls.Grid.SetRow(scroll, 1); diningScreen.Children.Add(scroll); host.Children.Add(diningScreen);
        diningLayoutSelector.SelectionChanged += (_, _) => RenderDiningFloor();
    }

    private void BuildFloorPlanEditorScreen()
    {
        if (HomeScreen.Parent is not System.Windows.Controls.Grid host) return;
        floorPlanEditorView = new FloorPlanEditorView(scopeFactory, session);
        floorPlanEditorView.DoneRequested += async (_, _) =>
        {
            await LoadDiningFloorAsync();
            ShowScreen(HomeScreen);
        };
        floorPlanEditorScreen.Children.Add(floorPlanEditorView);
        host.Children.Add(floorPlanEditorScreen);
    }

    private void ConfigureRoleAccountForm()
    {
        NewStaffNameInput.Visibility = Visibility.Collapsed;
        NewStaffRoleSelector.Visibility = Visibility.Collapsed;
        staffRoleSelector.ItemsSource = new[] { UserRole.Manager, UserRole.Cashier }; staffRoleSelector.SelectedIndex = 0;
        if (NewStaffNameInput.Parent is System.Windows.Controls.StackPanel form)
        {
            var nameLabel = form.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault(x => x.Inlines.OfType<System.Windows.Documents.Run>().Any(r => r.Text == "Name"));
            if (nameLabel is not null) { nameLabel.Text = "Role *"; form.Children.Insert(form.Children.IndexOf(NewStaffNameInput) + 1, staffRoleSelector); }
            var createButton = form.Children.OfType<System.Windows.Controls.Button>().First(x => Equals(x.Content, "Add staff member"));
            var confirmLabel = new System.Windows.Controls.TextBlock { Text = "Confirm PIN *", FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 6, 4, 0) };
            var index = form.Children.IndexOf(createButton); form.Children.Insert(index, confirmLabel); form.Children.Insert(index + 1, confirmStaffPinInput); createButton.Content = "Create account";
        }
    }

    private void ConfigureOperationalGrids()
    {
        foreach (var grid in new[] { MenuGrid, CartGrid, HeldOrdersGrid }) { grid.CanUserReorderColumns = false; grid.CanUserResizeColumns = false; grid.RowHeight = 34; grid.SetValue(System.Windows.Controls.ScrollViewer.VerticalScrollBarVisibilityProperty, System.Windows.Controls.ScrollBarVisibility.Auto); grid.SetValue(System.Windows.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty, System.Windows.Controls.ScrollBarVisibility.Disabled); }
        foreach (var grid in new[] { StaffGrid, HistoryGrid })
        {
            grid.Style = (Style)FindResource("ModernMenuGrid");
            grid.CanUserReorderColumns = false;
            grid.CanUserResizeColumns = false;
        }
        CartGrid.MaxHeight = 250;
        if (CartGrid.Columns.Count == 3)
        {
            CartGrid.Columns[1].Width = 92; CartGrid.Columns[2].Header = "Total"; CartGrid.Columns[2].Width = 82;
            CartGrid.Columns.Insert(2, new System.Windows.Controls.DataGridTextColumn { Header = "Rate", Binding = new System.Windows.Data.Binding(nameof(OrderLine.UnitPrice)) { StringFormat = "N2" }, Width = 82 });
            CartGrid.Columns.Insert(3, new System.Windows.Controls.DataGridTextColumn { Header = "Mode", Binding = new System.Windows.Data.Binding(nameof(OrderLine.PreparationMode)), Width = 76 });
        }
    }

    private void ConfigureMenuEditor()
    {
        if (AdminMenuGrid.Parent is not System.Windows.Controls.Grid host) return;
        var editor = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        editor.Children.Add(new System.Windows.Controls.TextBlock { Text = "Edit selected item", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        editor.Children.Add(editMenuCategorySelector); editor.Children.Add(editMenuNameInput); editor.Children.Add(editMenuPriceInput);
        var update = new System.Windows.Controls.Button { Content = "Save changes" }; update.Click += UpdateMenuItem_Click; editor.Children.Add(update);
        menuEditorCard = new System.Windows.Controls.Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(96, 165, 250)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed, Child = editor };
        var row = new System.Windows.Controls.RowDefinition { Height = GridLength.Auto }; host.RowDefinitions.Insert(3, row);
        System.Windows.Controls.Grid.SetRow(menuEditorCard, 3); host.Children.Add(menuEditorCard);
        foreach (FrameworkElement child in host.Children) if (child != menuEditorCard && System.Windows.Controls.Grid.GetRow(child) >= 3) System.Windows.Controls.Grid.SetRow(child, System.Windows.Controls.Grid.GetRow(child) + 1);
        AdminMenuGrid.SelectionChanged += (_, _) => PopulateMenuEditor();
    }

    private void ConfigureNumericColumnAlignment()
    {
        var numericHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Price", "Price (INR)", "Rate", "Amount", "Total", "Seats", "Column", "Row", "Width", "Height" };
        var valueStyle = (Style)FindResource("RightAlignedCellText");
        var textStyle = (Style)FindResource("CenteredCellText");
        var headerStyle = (Style)FindResource("RightAlignedHeader");
        foreach (var grid in new[] { MenuGrid, CartGrid, HeldOrdersGrid, PaymentsGrid, AdminMenuGrid, StaffGrid, HistoryGrid, AuditGrid })
        {
            foreach (var column in grid.Columns.OfType<System.Windows.Controls.DataGridTextColumn>())
            {
                if (numericHeaders.Contains(column.Header?.ToString() ?? string.Empty))
                {
                    column.ElementStyle = valueStyle;
                    column.HeaderStyle = headerStyle;
                }
                else column.ElementStyle ??= textStyle;
            }
        }
    }

    private void ConfigureApplicationAccountManagement()
    {
        if (ApplicationMaintenanceScreen.Child is not System.Windows.Controls.StackPanel host) return;
        applicationAccountGrid.Style = (Style)FindResource("ModernMenuGrid");
        applicationAccountGrid.CanUserReorderColumns = false;
        applicationAccountGrid.CanUserResizeColumns = false;
        applicationAccountGrid.Height = 190;
        applicationAccountGrid.Margin = new Thickness(0);
        applicationAccountGrid.RowHeight = 44;
        var centeredCellStyle = (Style)FindResource("CenteredStaffCellText");
        var centeredHeaderStyle = (Style)FindResource("CenteredStaffHeader");
        applicationAccountGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = "Account", Binding = new System.Windows.Data.Binding(nameof(AppUser.DisplayName)), Width = new System.Windows.Controls.DataGridLength(1.15, System.Windows.Controls.DataGridLengthUnitType.Star), ElementStyle = centeredCellStyle, HeaderStyle = centeredHeaderStyle });
        applicationAccountGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = "Role", Binding = new System.Windows.Data.Binding(nameof(AppUser.Role)), Width = new System.Windows.Controls.DataGridLength(0.85, System.Windows.Controls.DataGridLengthUnitType.Star), ElementStyle = centeredCellStyle, HeaderStyle = centeredHeaderStyle });

        applicationResetPinInput.Width = double.NaN;
        applicationResetPinInput.Height = 40;
        applicationResetPinInput.Margin = new Thickness(0, 7, 0, 10);
        applicationResetPinInput.HorizontalAlignment = HorizontalAlignment.Stretch;
        var resetButton = new System.Windows.Controls.Button { Content = "Reset selected account PIN" };
        resetButton.Style = (Style)FindResource("PrimaryButton");
        resetButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        resetButton.Click += ResetApplicationAccountPin_Click;

        var resetContent = new System.Windows.Controls.StackPanel();
        resetContent.Children.Add(new System.Windows.Controls.TextBlock { Text = "Assign new PIN", FontSize = 16, FontWeight = FontWeights.SemiBold });
        resetContent.Children.Add(new System.Windows.Controls.TextBlock { Text = "Enter at least four digits for the selected account.", Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 9) });
        resetContent.Children.Add(new System.Windows.Controls.TextBlock { Text = "New PIN *", FontWeight = FontWeights.SemiBold });
        resetContent.Children.Add(applicationResetPinInput);
        resetContent.Children.Add(resetButton);
        var resetCard = new System.Windows.Controls.Border { Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)), BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Child = resetContent };

        var workspace = new System.Windows.Controls.Grid { Margin = new Thickness(0, 12, 0, 0) };
        workspace.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(18) });
        workspace.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        workspace.Children.Add(applicationAccountGrid);
        System.Windows.Controls.Grid.SetColumn(resetCard, 2);
        workspace.Children.Add(resetCard);

        var content = new System.Windows.Controls.StackPanel();
        content.Children.Add(new System.Windows.Controls.TextBlock { Text = "Account password reset", FontSize = 18, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new System.Windows.Controls.TextBlock { Text = "Select another active account and assign a new PIN of at least four digits.", Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)), Margin = new Thickness(0, 3, 0, 0) });
        content.Children.Add(workspace);
        var card = new System.Windows.Controls.Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 14, 0, 0), Child = content };
        host.Children.Insert(host.Children.IndexOf(ApplicationStatusText), card);
    }

    private void ConfigureServerNameEditor()
    {
        if (ServerNameInput.Parent is not System.Windows.Controls.Panel serverRow) return;
        updateServerNameButton.Style = (Style)FindResource("CompactAction");
        updateServerNameButton.ToolTip = "Save the server name on this open order";
        updateServerNameButton.Click += UpdateServerName_Click;
        serverRow.Children.Add(updateServerNameButton);
    }

    private async Task LoadApplicationAccountsAsync()
    {
        if (!IsAdministrator) return;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var users = await scope.ServiceProvider.GetRequiredService<IAdministrationService>().GetStaffAsync();
            applicationAccountGrid.ItemsSource = users.Where(x => x.IsActive && x.Id != session.CurrentUser!.Id && x.Role != UserRole.Admin).ToList();
        }
        catch (Exception ex) { ApplicationStatusText.Text = ex.Message; }
    }

    private async void ResetApplicationAccountPin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (applicationAccountGrid.SelectedItem is not AppUser user) throw new InvalidOperationException("Select an account to reset its PIN.");
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IAdministrationService>().ResetStaffPinAsync(user.Id, applicationResetPinInput.Password, session.CurrentUser!.Id);
            applicationResetPinInput.Clear();
            ApplicationStatusText.Text = $"PIN reset for {user.DisplayName}.";
            await LoadApplicationAccountsAsync();
        }
        catch (Exception ex) { ApplicationStatusText.Text = ex.Message; }
    }

    private void PopulateMenuEditor()
    {
        if (AdminMenuGrid.SelectedItems.Count != 1 || AdminMenuGrid.SelectedItem is not MenuItem item)
        {
            if (menuEditorCard is not null) menuEditorCard.Visibility = Visibility.Collapsed;
            return;
        }
        if (menuEditorCard is not null) menuEditorCard.Visibility = Visibility.Visible;
        editMenuNameInput.Text = item.Name; editMenuPriceInput.Text = item.UnitPrice.ToString("0.##", CultureInfo.CurrentCulture);
        editMenuCategorySelector.SelectedItem = editMenuCategorySelector.Items.OfType<MenuCategory>().FirstOrDefault(x => x.Id == item.MenuCategoryId);
    }

    private async Task LoadDiningFloorAsync()
    {
        using var scope = scopeFactory.CreateScope();
        diningLayouts = await scope.ServiceProvider.GetRequiredService<IFloorPlanService>().GetLiveFloorPlansAsync();
        var selectedId = (diningLayoutSelector.SelectedItem as FloorPlanView)?.Id;
        diningLayoutSelector.ItemsSource = diningLayouts;
        diningLayoutSelector.SelectedItem = diningLayouts.FirstOrDefault(x => x.Id == selectedId) ?? diningLayouts.FirstOrDefault();
        RenderDiningFloor();
    }

    private void RenderDiningFloor()
    {
        diningFloorGrid.Children.Clear(); diningFloorGrid.RowDefinitions.Clear(); diningFloorGrid.ColumnDefinitions.Clear();
        if (diningLayoutSelector.SelectedItem is not FloorPlanView layout) return;
        var columns = Math.Max(8, layout.Tables.Select(x => x.GridX + x.GridWidth).DefaultIfEmpty(8).Max());
        var rows = Math.Max(5, layout.Tables.Select(x => x.GridY + x.GridHeight).DefaultIfEmpty(5).Max());
        for (var i = 0; i < columns; i++) diningFloorGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(125) });
        for (var i = 0; i < rows; i++) diningFloorGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(105) });
        foreach (var table in layout.Tables)
        {
            var button = new System.Windows.Controls.Button { Tag = table.Id, Margin = new Thickness(7), Background = table.State == "Occupied" ? new SolidColorBrush(Color.FromRgb(186, 230, 253)) : new SolidColorBrush(Color.FromRgb(241, 245, 249)), Content = new System.Windows.Controls.TextBlock { Text = table.State == "Available" ? $"{table.Name}\n{table.Capacity} seats\nAvailable" : $"{table.Name}\nOccupied\n{table.RunningTotal:N2}", TextAlignment = TextAlignment.Center } };
            button.Click += async (_, _) => await OpenTableOrderAsync((int)button.Tag);
            System.Windows.Controls.Grid.SetColumn(button, table.GridX); System.Windows.Controls.Grid.SetRow(button, table.GridY); System.Windows.Controls.Grid.SetColumnSpan(button, table.GridWidth); System.Windows.Controls.Grid.SetRowSpan(button, table.GridHeight); diningFloorGrid.Children.Add(button);
        }
    }
    private static void SetActiveNavigation(System.Windows.Controls.Button button, bool isActive)
    {
        button.Background = isActive ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : Brushes.Transparent;
        button.Foreground = isActive ? Brushes.White : new SolidColorBrush(Color.FromRgb(203, 213, 225));
    }
    private void Logout_Click(object sender, RoutedEventArgs e) => (System.Windows.Application.Current as App)?.Logout();
    private void ReorderManagerNavigation()
    {
        if (session.CurrentUser?.Role != UserRole.Manager || HomeNavButton.Parent is not System.Windows.Controls.StackPanel nav) return;
        var desired = new[] { HomeNavButton, diningNavButton, MenuManagementNavButton, HeldOrdersNavButton, AdminNavButton, ReportsNavButton };
        foreach (var button in desired) nav.Children.Remove(button);
        foreach (var button in desired) nav.Children.Add(button);
    }
    private void UpdateTakeawayQueueLabels()
    {
        foreach (var element in LogicalDescendants(HeldOrdersScreen))
        {
            if (element is System.Windows.Controls.TextBlock text && text.Text == "Held orders") text.Text = "Open takeaway orders";
            else if (element is System.Windows.Controls.TextBlock description && description.Text.StartsWith("Resume a saved bill")) description.Text = "Open an unfinished takeaway order or start a new one from Home.";
            else if (element is System.Windows.Controls.Button button && Equals(button.Content, "Resume selected order")) button.Content = "Open selected order";
        }
    }
    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        { yield return child; foreach (var descendant in LogicalDescendants(child)) yield return descendant; }
    }
    private async void BackupNow_Click(object sender, RoutedEventArgs e) { if (!IsAdministrator) return; try { await backupScheduler.CreateNowAsync(); ApplicationStatusText.Text = "Local database backup created."; } catch (Exception ex) { ApplicationStatusText.Text = ex.Message; } }
    private async void EditFloorPlan_Click(object sender, RoutedEventArgs e)
    {
        if (session.CurrentUser?.Role != UserRole.Manager) return;
        if (floorPlanEditorView is null) return;
        await floorPlanEditorView.ReloadAsync();
        ShowScreen(floorPlanEditorScreen);
    }

    private async void HomeDining_Click(object sender, RoutedEventArgs e) => await OpenDiningFloorAsync();
    private async Task OpenDiningFloorAsync()
    {
        if (IsAdministrator) return;
        await LoadDiningFloorAsync();
        ShowScreen(diningScreen);
    }

    private async void HomeTakeaway_Click(object sender, RoutedEventArgs e)
    {
        ShowScreen(PosScreen);
        await StartTakeawayAsync();
    }

    private void BeginDineIn_Click(object sender, RoutedEventArgs e)
    {
        if (IsAdministrator) return;
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
        await OpenTableOrderAsync(table.Id);
    }

    private async Task OpenTableOrderAsync(int tableId)
    {
        if (IsAdministrator) return;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var table = await scope.ServiceProvider.GetRequiredService<RestaurantDbContext>().DiningTables.Include(x => x.FloorLayout).SingleAsync(x => x.Id == tableId);
            selectedTableLabel = table.FloorLayout is null ? table.Name : $"{table.FloorLayout.Name} • {table.Name}";
            currentOrder = await scope.ServiceProvider.GetRequiredService<IOrderWorkflow>()
                .FindActiveTableOrderAsync(tableId, session.CurrentUser!.Id);
            ServerNameInput.Text = currentOrder?.ServerName ?? string.Empty;
            pendingOrderType = currentOrder is null ? OrderType.DineIn : null; pendingTableId = currentOrder is null ? tableId : null;
            invoicePrintedForCurrentOrder = false;
            ShowScreen(PosScreen);
            RefreshOrder(currentOrder is null ? "Table selected. Add the first item to start its order." : "Existing table order opened.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void TakeawayOrder_Click(object sender, RoutedEventArgs e)
        => await StartTakeawayAsync();

    private Task StartTakeawayAsync()
    {
        choosingDineIn = false;
        TableSelector.SelectedItem = null;
        TableSelectionPanel.Visibility = Visibility.Collapsed;
        DineInButton.Style = (Style)FindResource("CompactAction");
        TakeawayButton.Style = (Style)FindResource("PrimaryButton");
        ServerNameInput.Clear();
        currentOrder = null; pendingOrderType = OrderType.Takeaway; pendingTableId = null; selectedTableLabel = null; ShowScreen(PosScreen); RefreshOrder("New takeaway. Add the first item to start the order.");
        return Task.CompletedTask;
    }
    private async Task LoadHeldOrdersAsync()
    {
        try { using var scope = scopeFactory.CreateScope(); HeldOrdersGrid.ItemsSource = await scope.ServiceProvider.GetRequiredService<IOrderWorkflow>().GetOpenTakeawayOrdersAsync(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void RefreshHeldOrders_Click(object sender, RoutedEventArgs e) => await LoadHeldOrdersAsync();
    private async void ResumeHeldOrder_Click(object sender, RoutedEventArgs e) => await ResumeSelectedHeldOrderAsync();
    private async void HeldOrdersGrid_DoubleClick(object sender, MouseButtonEventArgs e) => await ResumeSelectedHeldOrderAsync();
    private async Task ResumeSelectedHeldOrderAsync()
    {
        if (HeldOrdersGrid.SelectedItem is not Order order) return;
        try { using var scope = scopeFactory.CreateScope(); currentOrder = await scope.ServiceProvider.GetRequiredService<IOrderWorkflow>().OpenTakeawayAsync(order.Id, session.CurrentUser!.Id); ServerNameInput.Text = currentOrder.ServerName; invoicePrintedForCurrentOrder = false; ShowScreen(PosScreen); RefreshOrder("Takeaway order opened."); }
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
            AverageBillText.Text = $"INR {(report.PaidOrderCount == 0 ? 0 : report.SalesTotal / report.PaidOrderCount):N2}";
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
            managementCategorySelector.ItemsSource = await admin.GetCategoriesAsync();
            GstRateInput.Text = (await admin.GetSettingsAsync()).GstRate.ToString("0.##", CultureInfo.CurrentCulture);
            if (managementCategorySelector.SelectedIndex < 0) managementCategorySelector.SelectedIndex = 0;
            await LoadHistoryAsync(admin);
        }
        catch (Exception ex) { AdminStatusText.Text = ex.Message; }
    }
    private async void AddStaff_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (staffRoleSelector.SelectedItem is not UserRole role) throw new InvalidOperationException("Select a staff role.");
            if (NewStaffPinInput.Password != confirmStaffPinInput.Password) throw new InvalidOperationException("PIN and confirmation do not match.");
            using var scope = scopeFactory.CreateScope(); await scope.ServiceProvider.GetRequiredService<IAdministrationService>().AddStaffAsync(role, NewStaffPinInput.Password, session.CurrentUser!.Id);
            NewStaffPinInput.Clear(); confirmStaffPinInput.Clear(); AdminStatusText.Text = $"{role} account created."; await LoadAdminDataAsync();
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
            if (managementCategorySelector.SelectedItem is not MenuCategory category || !decimal.TryParse(NewMenuPriceInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var price)) throw new InvalidOperationException("Choose a category and enter a valid price greater than 0.");
            if (price <= 0) throw new InvalidOperationException("Menu item price must be greater than 0.");
            using var scope = scopeFactory.CreateScope(); await scope.ServiceProvider.GetRequiredService<IAdministrationService>().AddMenuItemAsync(category.Id, NewMenuItemNameInput.Text, price, session.CurrentUser!.Id);
            NewMenuItemNameInput.Clear(); NewMenuPriceInput.Clear(); MenuManagementStatusText.Text = "Menu item added."; await ReloadMenuAsync(); await LoadMenuManagementAsync();
        }
        catch (Exception ex) { MenuManagementStatusText.Text = ex.Message; }
    }
    private async void UpdateMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AdminMenuGrid.SelectedItems.Count != 1 || AdminMenuGrid.SelectedItem is not MenuItem item) throw new InvalidOperationException("Select one menu item to edit.");
            if (editMenuCategorySelector.SelectedItem is not MenuCategory category || !decimal.TryParse(editMenuPriceInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var price)) throw new InvalidOperationException("Choose a category and enter a valid price greater than 0.");
            if (price <= 0) throw new InvalidOperationException("Menu item price must be greater than 0.");
            using var scope = scopeFactory.CreateScope(); await scope.ServiceProvider.GetRequiredService<IAdministrationService>().UpdateMenuItemAsync(item.Id, category.Id, editMenuNameInput.Text, price, session.CurrentUser!.Id);
            MenuManagementStatusText.Text = "Menu item updated. Existing order lines retain their captured name and price."; await ReloadMenuAsync(); await LoadMenuManagementAsync();
        }
        catch (Exception ex) { MenuManagementStatusText.Text = ex.Message; }
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
            var categories = await admin.GetCategoriesAsync();
            managementCategorySelector.ItemsSource = categories; editMenuCategorySelector.ItemsSource = categories;
            if (managementCategorySelector.SelectedIndex < 0) managementCategorySelector.SelectedIndex = 0;
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
        if (currentOrder is null && pendingOrderType is OrderType type)
        {
            try { using var scope = scopeFactory.CreateScope(); currentOrder = await scope.ServiceProvider.GetRequiredService<IOrderWorkflow>().StartWithMenuItemAsync(type, pendingTableId, item.Id, type == OrderType.Takeaway ? PreparationMode.Packed : PreparationMode.DineIn, session.CurrentUser!.Id, ServerNameInput.Text); pendingOrderType = null; pendingTableId = null; RefreshOrder($"Started order with {item.Name}."); }
            catch (Exception ex) { ShowError(ex); }
            return;
        }
        if (currentOrder is null) { StatusText.Text = "Choose Dining or Takeaway first."; return; }
        await ApplyAsync(w => w.AddMenuItemAsync(currentOrder.Id, item.Id, session.CurrentUser!.Id), $"Added {item.Name}.");
    }
    private void MenuSearchInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyMenuFilter();
    private void ApplyMenuFilter()
    {
        var query = MenuSearchInput?.Text.Trim() ?? string.Empty;
        var categoryId = (menuCategoryFilter.SelectedItem as MenuCategory)?.Id ?? 0;
        MenuGrid.ItemsSource = menuItems.Where(x => (categoryId == 0 || x.MenuCategoryId == categoryId) && (string.IsNullOrWhiteSpace(query) || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || x.MenuCategory?.Name.Contains(query, StringComparison.OrdinalIgnoreCase) == true)).ToList();
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
    private async void UpdateServerName_Click(object sender, RoutedEventArgs e)
    {
        if (currentOrder?.Status != OrderStatus.Open) return;
        await ApplyAsync(w => w.SetServerNameAsync(currentOrder.Id, ServerNameInput.Text, session.CurrentUser!.Id), "Server name updated.");
    }
    private void PercentageDiscount_Click(object sender, RoutedEventArgs e) => SelectDiscountType(DiscountType.Percentage);
    private void FixedDiscount_Click(object sender, RoutedEventArgs e) => SelectDiscountType(DiscountType.FixedAmount);
    private void SelectDiscountType(DiscountType type)
    {
        selectedDiscountType = type;
        PercentageDiscountButton.Style = (Style)FindResource(type == DiscountType.Percentage ? "PrimaryButton" : "CompactAction");
        FixedDiscountButton.Style = (Style)FindResource(type == DiscountType.FixedAmount ? "PrimaryButton" : "CompactAction");
    }
    private async void LeaveOrder_Click(object sender, RoutedEventArgs e)
    {
        if (currentOrder is null) { pendingOrderType = null; pendingTableId = null; RefreshOrder("No order was created."); ShowScreen(HomeScreen); return; }
        if (currentOrder.Status != OrderStatus.Open) return;
        if (currentOrder.Type == OrderType.DineIn)
        {
            currentOrder = null;
            RefreshOrder("The dine-in order remains open on its table.");
            ShowScreen(HomeScreen);
            await OpenDiningFloorAsync();
            return;
        }

        StatusText.Text = "Use Save open takeaway or Pay / Complete before leaving this order.";
    }
    private async void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (currentOrder is null) return;
        await ApplyAsync(w => w.TakePaymentAsync(currentOrder.Id, selectedPaymentMethod, currentOrder.GrandTotal, session.CurrentUser!.Id), "Payment recorded.");
        if (currentOrder?.Status != OrderStatus.Paid) return;
        var paidOrder = currentOrder;
        var printMessage = await PrintReceiptAsync(paidOrder, false);
        currentOrder = null; pendingOrderType = null; pendingTableId = null; selectedTableLabel = null; invoicePrintedForCurrentOrder = false;
        RefreshOrder(string.Empty); ShowScreen(HomeScreen);
        HomeStaffText.Text = $"{printMessage} Signed in as {session.CurrentUser!.DisplayName} ({session.CurrentUser.Role}).";
    }
    private async void HoldTakeaway_Click(object sender, RoutedEventArgs e)
    {
        if (currentOrder?.Type != OrderType.Takeaway) return; await ApplyAsync(w => w.HoldTakeawayAsync(currentOrder.Id, session.CurrentUser!.Id), "Takeaway saved open."); currentOrder = null; RefreshOrder("Takeaway is available in Open takeaways."); ShowScreen(HomeScreen);
    }
    private async void TogglePacked_Click(object sender, RoutedEventArgs e) { if (currentOrder?.Type != OrderType.DineIn || CartGrid.SelectedItem is not OrderLine line) return; var mode = line.PreparationMode == PreparationMode.Packed ? PreparationMode.DineIn : PreparationMode.Packed; await ApplyAsync(w => w.SetLinePreparationModeAsync(currentOrder.Id, line.Id, mode, session.CurrentUser!.Id), $"{line.ItemName} marked {mode}."); UpdatePreparationAction(); }
    private void UpdatePreparationAction() { preparationModeButton.Visibility = currentOrder?.Type == OrderType.DineIn ? Visibility.Visible : Visibility.Collapsed; preparationModeButton.Content = CartGrid.SelectedItem is OrderLine { PreparationMode: PreparationMode.Packed } ? "Mark selected as Dine-In" : "Mark selected as Packed"; }
    private async void CancelOrder_Click(object sender, RoutedEventArgs e)
    {
        if (currentOrder is null) { pendingOrderType = null; pendingTableId = null; selectedTableLabel = null; ShowScreen(HomeScreen); return; }
        var context = currentOrder.Type == OrderType.DineIn ? selectedTableLabel ?? currentOrder.DiningTable?.Name ?? "this table" : "this takeaway order";
        if (MessageBox.Show(this, $"Cancel {context}?\n\nThis will close the running order and cannot be undone.", "Confirm order cancellation", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            var wasDineIn = currentOrder.Type == OrderType.DineIn;
            using var scope = scopeFactory.CreateScope(); await scope.ServiceProvider.GetRequiredService<IOrderWorkflow>().CancelAsync(currentOrder.Id, session.CurrentUser!.Id);
            currentOrder = null; pendingOrderType = null; pendingTableId = null; selectedTableLabel = null; RefreshOrder("Order cancelled.");
            if (wasDineIn) { await LoadDiningFloorAsync(); ShowScreen(diningScreen); } else ShowScreen(HomeScreen);
        }
        catch (Exception ex) { ShowError(ex); }
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
    private async void Reprint_Click(object sender, RoutedEventArgs e) { if (currentOrder is not null) await PrintReceiptAsync(currentOrder, true); }
    private async Task<string> PrintReceiptAsync(Order order, bool isReprint)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var printed = await scope.ServiceProvider.GetRequiredService<IReceiptPrinter>().PrintAsync(order, isReprint);
            if (!printed) { StatusText.Text = "Payment completed; printing was cancelled."; return "Payment completed; printing was cancelled."; }
            invoicePrintedForCurrentOrder = true;
            ReprintButton.Visibility = Visibility.Visible;
            StatusText.Text = isReprint ? "Invoice sent for reprint." : "Invoice sent to the printer.";
            return StatusText.Text;
        }
        catch (Exception ex) { StatusText.Text = $"Payment completed; invoice printing failed: {ex.Message}"; return StatusText.Text; }
    }
    private async Task ApplyAsync(Func<IOrderWorkflow, Task<Order>> action, string message) { try { using var scope = scopeFactory.CreateScope(); currentOrder = await action(scope.ServiceProvider.GetRequiredService<IOrderWorkflow>()); RefreshOrder(message); } catch (Exception ex) { ShowError(ex); } }
    private void RefreshOrder(string message)
    {
        CartGrid.ItemsSource = currentOrder?.Lines.ToList();
        var isTakeaway = currentOrder?.Type == OrderType.Takeaway || pendingOrderType == OrderType.Takeaway;
        var dineInLabel = selectedTableLabel ?? currentOrder?.DiningTable?.Name ?? "Selected table";
        var serverDetail = currentOrder is null ? string.Empty : string.IsNullOrWhiteSpace(currentOrder.ServerName) ? "Server: Not specified" : $"Server: {currentOrder.ServerName}";
        orderContextBanner.Text = isTakeaway ? "TAKEAWAY ORDER" : currentOrder?.Type == OrderType.DineIn || pendingOrderType == OrderType.DineIn ? $"DINE-IN • {dineInLabel}" : "NO ORDER SELECTED";
        OrderInfoText.Text = currentOrder is null ? pendingOrderType is null ? "No active order" : isTakeaway ? "New takeaway order (not saved yet)" : $"New dine-in order for {dineInLabel} (not saved yet)" : isTakeaway ? $"{currentOrder.InvoiceNumber} • Takeaway • {serverDetail} • {currentOrder.Status}" : $"{currentOrder.InvoiceNumber} • {dineInLabel} • {serverDetail} • {currentOrder.Status}";
        ServerNameInput.IsEnabled = pendingOrderType is not null || currentOrder?.Status == OrderStatus.Open;
        updateServerNameButton.Visibility = currentOrder?.Status == OrderStatus.Open ? Visibility.Visible : Visibility.Collapsed;
        holdTakeawayButton.Visibility = isTakeaway ? Visibility.Visible : Visibility.Collapsed;
        holdTakeawayButton.IsEnabled = currentOrder?.Type == OrderType.Takeaway && currentOrder.Status == OrderStatus.Open;
        LeaveOrderButton.Content = currentOrder?.Type == OrderType.DineIn ? "Return to floor plan" : "Return to home";
        BillDiscountTotalText.Text = currentOrder is null ? string.Empty : currentOrder.DiscountAmount.ToString("N2", CultureInfo.CurrentCulture);
        GstTotalText.Text = currentOrder is null ? string.Empty : currentOrder.TaxAmount.ToString("N2", CultureInfo.CurrentCulture);
        GrandTotalText.Text = currentOrder is null ? string.Empty : currentOrder.GrandTotal.ToString("N2", CultureInfo.CurrentCulture);
        ReprintButton.Visibility = currentOrder?.Status == OrderStatus.Paid && invoicePrintedForCurrentOrder ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreparationAction();
        if (currentOrder is not null) { SelectDiscountType(currentOrder.DiscountType == DiscountType.None ? DiscountType.Percentage : currentOrder.DiscountType); BillDiscountValueInput.Text = currentOrder.DiscountValue.ToString("0.##", CultureInfo.CurrentCulture); }
        StatusText.Text = message; OnPropertyChanged(nameof(HasActiveOrder)); OnPropertyChanged(nameof(HasPaidOrder));
    }
    private void ShowError(Exception ex) { if (IsAdministrator) { ShowScreen(ApplicationMaintenanceScreen); ApplicationStatusText.Text = ex.Message; } else { ShowScreen(PosScreen); StatusText.Text = ex.Message; } }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
