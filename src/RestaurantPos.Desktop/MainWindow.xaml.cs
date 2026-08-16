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
    private Order? lastPaidOrder;
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
    private InteractiveFloorPlanEditorView? floorPlanEditorView;
    private readonly System.Windows.Controls.ComboBox diningLayoutSelector = new() { Width = 220, DisplayMemberPath = "Name", Margin = new Thickness(0, 5, 0, 0) };
    private readonly System.Windows.Controls.Grid diningFloorGrid = new() { Background = Brushes.Transparent, Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly System.Windows.Controls.TextBlock diningFloorNameText = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(23, 32, 51)) };
    private readonly System.Windows.Controls.TextBlock diningFloorDescriptionText = new() { Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), Margin = new Thickness(0, 3, 0, 0) };
    private readonly System.Windows.Controls.TextBlock diningTotalCountText = new() { FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)) };
    private readonly System.Windows.Controls.TextBlock diningAvailableCountText = new() { FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61)) };
    private readonly System.Windows.Controls.TextBlock diningOccupiedCountText = new() { FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(29, 78, 216)) };
    private readonly System.Windows.Controls.Button diningNavButton = new() { Content = "DINE-IN" };
    private IReadOnlyList<FloorPlanView> diningLayouts = [];
    private readonly System.Windows.Controls.Button preparationModeButton = new() { Content = "Mark selected as Packed" };
    private readonly System.Windows.Controls.Button holdTakeawayButton = new() { Content = "Save open takeaway", Visibility = Visibility.Collapsed };
    private readonly System.Windows.Controls.Button updateServerNameButton = new() { Content = "Update server", Visibility = Visibility.Collapsed };
    private FrameworkElement? serverNameEditorRow;
    private readonly System.Windows.Controls.TextBlock orderContextBanner = new() { FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(30, 58, 138)), Background = new SolidColorBrush(Color.FromRgb(219, 234, 254)), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 9) };
    private readonly System.Windows.Controls.ComboBox editMenuCategorySelector = new() { MinWidth = 165, DisplayMemberPath = "Name", Margin = new Thickness(4) };
    private readonly System.Windows.Controls.TextBox editMenuNameInput = new() { MinWidth = 190, Margin = new Thickness(4) };
    private readonly System.Windows.Controls.TextBox editMenuPriceInput = new() { Width = 110, Margin = new Thickness(4), TextAlignment = TextAlignment.Right };
    private System.Windows.Controls.Border? menuEditorCard;
    private bool choosingDineIn;
    private DiscountType selectedDiscountType = DiscountType.Percentage;
    private PaymentMethod selectedPaymentMethod = PaymentMethod.Cash;
    private ReceiptPaperWidth configuredReceiptPaperWidth = ReceiptPaperWidth.Mm80;
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
        StateChanged += (_, _) => RestoreWindowButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void WorkspaceTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2) { ToggleWindowState(); return; }
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void RestoreWindow_Click(object sender, RoutedEventArgs e) => ToggleWindowState();
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
    private void ToggleWindowState() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private async void LoadData(object sender, RoutedEventArgs e)
    {
        using var scope = scopeFactory.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        menuItems = await db.MenuItems.Include(x => x.MenuCategory).Where(x => x.IsActive).OrderBy(x => x.MenuCategory!.SortOrder).ThenBy(x => x.SortOrder).ToListAsync();
        ApplyMenuFilter();
        menuCategoryFilter.ItemsSource = new[] { new MenuCategory { Id = 0, Name = "All categories" } }.Concat(menuItems.Select(x => x.MenuCategory!).DistinctBy(x => x.Id)).ToList(); menuCategoryFilter.SelectedIndex = 0;
        TableSelector.ItemsSource = await db.DiningTables.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        var restaurantSettings = await db.RestaurantSettings.SingleAsync(x => x.Id == 1);
        configuredReceiptPaperWidth = restaurantSettings.ReceiptPaperWidthMm == 58 ? ReceiptPaperWidth.Mm58 : ReceiptPaperWidth.Mm80;
        ReceiptPaperSelector.SelectedIndex = configuredReceiptPaperWidth == ReceiptPaperWidth.Mm58 ? 0 : 1;
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
        var headerCard = new System.Windows.Controls.Border { Style = (Style)FindResource("Card"), Padding = new Thickness(22, 18, 22, 18), Margin = new Thickness(0, 0, 0, 16) };
        var header = new System.Windows.Controls.Grid();
        header.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
        header.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
        var title = new System.Windows.Controls.StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new System.Windows.Controls.TextBlock { Text = "DINE-IN", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235)) });
        title.Children.Add(new System.Windows.Controls.TextBlock { Text = "Dining floor", FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(23, 32, 51)), Margin = new Thickness(0, 2, 0, 0) });
        title.Children.Add(new System.Windows.Controls.TextBlock { Text = "Choose an available table or reopen an occupied table's order.", Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), Margin = new Thickness(0, 4, 0, 0) });
        header.Children.Add(title);
        var floorSelector = new System.Windows.Controls.StackPanel { Margin = new Thickness(24, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
        floorSelector.Children.Add(new System.Windows.Controls.TextBlock { Text = "Floor", FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)) });
        floorSelector.Children.Add(diningLayoutSelector);
        System.Windows.Automation.AutomationProperties.SetName(diningLayoutSelector, "Dining floor selector");
        System.Windows.Controls.Grid.SetColumn(floorSelector, 1); header.Children.Add(floorSelector);
        var refresh = new System.Windows.Controls.Button { Content = "Refresh floor", Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(16, 10, 16, 10), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(refresh, "Refresh dining floor");
        refresh.Click += async (_, _) => await LoadDiningFloorAsync();
        System.Windows.Controls.Grid.SetColumn(refresh, 2); header.Children.Add(refresh);
        headerCard.Child = header;
        diningScreen.Children.Add(headerCard);

        var floorCard = new System.Windows.Controls.Border { Style = (Style)FindResource("Card"), Padding = new Thickness(0), Background = Brushes.White };
        var floorLayout = new System.Windows.Controls.Grid();
        floorLayout.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        floorLayout.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
        var floorToolbar = new System.Windows.Controls.Grid { Margin = new Thickness(20, 16, 20, 14) };
        floorToolbar.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
        floorToolbar.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
        var floorIdentity = new System.Windows.Controls.StackPanel();
        floorIdentity.Children.Add(diningFloorNameText); floorIdentity.Children.Add(diningFloorDescriptionText); floorToolbar.Children.Add(floorIdentity);
        var summary = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        summary.Children.Add(CreateDiningSummaryBadge(diningTotalCountText, new SolidColorBrush(Color.FromRgb(241, 245, 249))));
        summary.Children.Add(CreateDiningSummaryBadge(diningAvailableCountText, new SolidColorBrush(Color.FromRgb(240, 253, 244))));
        summary.Children.Add(CreateDiningSummaryBadge(diningOccupiedCountText, new SolidColorBrush(Color.FromRgb(239, 246, 255))));
        System.Windows.Controls.Grid.SetColumn(summary, 1); floorToolbar.Children.Add(summary);
        floorLayout.Children.Add(floorToolbar);
        var scroll = new System.Windows.Controls.ScrollViewer { Content = diningFloorGrid, Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)), HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, Padding = new Thickness(4) };
        System.Windows.Controls.Grid.SetRow(scroll, 1); floorLayout.Children.Add(scroll);
        floorCard.Child = floorLayout;
        System.Windows.Controls.Grid.SetRow(floorCard, 1); diningScreen.Children.Add(floorCard); host.Children.Add(diningScreen);
        diningLayoutSelector.SelectionChanged += (_, _) => RenderDiningFloor();
    }

    private static System.Windows.Controls.Border CreateDiningSummaryBadge(System.Windows.Controls.TextBlock text, Brush background) => new()
    {
        Background = background,
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(12, 7, 12, 7),
        Margin = new Thickness(6, 0, 0, 0),
        Child = text
    };

    private void BuildFloorPlanEditorScreen()
    {
        if (HomeScreen.Parent is not System.Windows.Controls.Grid host) return;
        floorPlanEditorView = new InteractiveFloorPlanEditorView(scopeFactory, session);
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
        CartGrid.Style = (Style)FindResource("ModernMenuGrid");
        CartGrid.RowHeight = 46;
        CartGrid.ColumnHeaderHeight = 40;
        CartGrid.MaxHeight = 250;
        HeldOrdersGrid.Style = (Style)FindResource("ModernMenuGrid");
        HeldOrdersGrid.RowHeight = 54;
        HeldOrdersGrid.ColumnHeaderHeight = 40;
        if (CartGrid.Columns.Count == 3)
        {
            var centeredCellStyle = (Style)FindResource("CenteredStaffCellText");
            var centeredHeaderStyle = (Style)FindResource("CenteredStaffHeader");
            var itemCellStyle = new Style(typeof(System.Windows.Controls.TextBlock), (Style)FindResource("CenteredCellText"));
            itemCellStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(16, 0, 10, 0)));

            if (CartGrid.Columns[0] is System.Windows.Controls.DataGridTextColumn itemColumn) itemColumn.ElementStyle = itemCellStyle;
            CartGrid.Columns[1].Width = 112;
            CartGrid.Columns[1].HeaderStyle = centeredHeaderStyle;
            CartGrid.Columns[2].Header = "Total";
            CartGrid.Columns[2].Width = 100;
            CartGrid.Columns.Insert(2, new System.Windows.Controls.DataGridTextColumn { Header = "Rate", Binding = new System.Windows.Data.Binding(nameof(OrderLine.UnitPrice)) { StringFormat = "N2" }, Width = 90 });
            CartGrid.Columns.Insert(3, new System.Windows.Controls.DataGridTextColumn { Header = "Mode", Binding = new System.Windows.Data.Binding(nameof(OrderLine.PreparationMode)), Width = 90, ElementStyle = centeredCellStyle, HeaderStyle = centeredHeaderStyle });
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
        if (serverRow.Parent is System.Windows.Controls.Panel legacyHost) legacyHost.Children.Remove(serverRow);
        if (OrderInfoText.Parent is not System.Windows.Controls.Panel orderDetails) return;

        serverNameEditorRow = serverRow;
        serverNameEditorRow.Margin = new Thickness(0, 6, 0, 4);
        serverNameEditorRow.Visibility = Visibility.Collapsed;
        if (serverRow.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault() is { } serverLabel)
        {
            serverLabel.MinWidth = 155;
            serverLabel.VerticalAlignment = VerticalAlignment.Center;
        }
        ServerNameInput.Width = 240;
        ServerNameInput.Height = 40;
        ServerNameInput.Margin = new Thickness(12, 0, 8, 0);
        ServerNameInput.Padding = new Thickness(10, 0, 10, 0);
        ServerNameInput.AcceptsReturn = false;
        ServerNameInput.TextWrapping = TextWrapping.NoWrap;
        ServerNameInput.VerticalContentAlignment = VerticalAlignment.Center;
        ServerNameInput.SetValue(System.Windows.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty, System.Windows.Controls.ScrollBarVisibility.Disabled);
        ServerNameInput.SetValue(System.Windows.Controls.ScrollViewer.VerticalScrollBarVisibilityProperty, System.Windows.Controls.ScrollBarVisibility.Disabled);
        updateServerNameButton.Style = (Style)FindResource("CompactAction");
        updateServerNameButton.Width = 120;
        updateServerNameButton.Height = 40;
        updateServerNameButton.Margin = new Thickness(0);
        updateServerNameButton.Padding = new Thickness(12, 0, 12, 0);
        updateServerNameButton.VerticalAlignment = VerticalAlignment.Center;
        updateServerNameButton.ToolTip = "Save the server name on this open order";
        updateServerNameButton.Click += UpdateServerName_Click;
        serverRow.Children.Add(updateServerNameButton);
        orderDetails.Children.Insert(orderDetails.Children.IndexOf(OrderInfoText), serverRow);
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
        if (diningLayoutSelector.SelectedItem is not FloorPlanView layout)
        {
            diningFloorNameText.Text = "No dining floor available";
            diningFloorDescriptionText.Text = "Create a floor plan from Restaurant management to begin.";
            diningTotalCountText.Text = "0 tables"; diningAvailableCountText.Text = "0 available"; diningOccupiedCountText.Text = "0 occupied";
            return;
        }
        var occupiedCount = layout.Tables.Count(x => x.State == "Occupied");
        var availableCount = layout.Tables.Count - occupiedCount;
        diningFloorNameText.Text = layout.Name;
        diningFloorDescriptionText.Text = $"{layout.Tables.Count} configured table{(layout.Tables.Count == 1 ? string.Empty : "s")} • Select a table to continue";
        diningTotalCountText.Text = $"{layout.Tables.Count} total";
        diningAvailableCountText.Text = $"{availableCount} available";
        diningOccupiedCountText.Text = $"{occupiedCount} occupied";
        var columns = Math.Max(1, layout.Tables.Select(x => x.GridX + x.GridWidth).DefaultIfEmpty(1).Max());
        var rows = Math.Max(1, layout.Tables.Select(x => x.GridY + x.GridHeight).DefaultIfEmpty(1).Max());
        for (var i = 0; i < columns; i++) diningFloorGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(150) });
        for (var i = 0; i < rows; i++) diningFloorGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(130) });
        foreach (var table in layout.Tables)
        {
            var isOccupied = table.State == "Occupied";
            var content = new System.Windows.Controls.StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
            var name = new System.Windows.Controls.TextBlock { Text = table.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(23, 32, 51)), TextWrapping = TextWrapping.Wrap };
            content.Children.Add(name);
            var statePill = new System.Windows.Controls.Border { Background = new SolidColorBrush(isOccupied ? Color.FromRgb(219, 234, 254) : Color.FromRgb(220, 252, 231)), CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 3, 7, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            statePill.Child = new System.Windows.Controls.TextBlock { Text = isOccupied ? "OCCUPIED" : "AVAILABLE", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(isOccupied ? Color.FromRgb(29, 78, 216) : Color.FromRgb(21, 128, 61)) };
            content.Children.Add(statePill);
            var details = new System.Windows.Controls.StackPanel { Margin = new Thickness(0, 9, 0, 0) };
            details.Children.Add(new System.Windows.Controls.TextBlock { Text = $"{table.Capacity} seats", Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)) });
            if (!string.IsNullOrWhiteSpace(table.Section)) details.Children.Add(new System.Windows.Controls.TextBlock { Text = table.Section, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            content.Children.Add(details);
            if (isOccupied)
            {
                var runningTotal = new System.Windows.Controls.TextBlock { Text = $"Running total  {table.RunningTotal:N2}", FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(29, 78, 216)), Margin = new Thickness(0, 6, 0, 0) };
                content.Children.Add(runningTotal);
            }
            var button = new System.Windows.Controls.Button { Tag = table.Id, Margin = new Thickness(7), Style = (Style)FindResource("DiningTableButton"), Background = isOccupied ? new SolidColorBrush(Color.FromRgb(248, 251, 255)) : Brushes.White, BorderBrush = new SolidColorBrush(isOccupied ? Color.FromRgb(147, 197, 253) : Color.FromRgb(220, 228, 239)), Content = content, ToolTip = isOccupied ? $"Open the running order for {table.Name}" : $"Start a new order for {table.Name}" };
            System.Windows.Automation.AutomationProperties.SetName(button, isOccupied ? $"{table.Name}, occupied, running total {table.RunningTotal:N2}" : $"{table.Name}, available, {table.Capacity} seats");
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
            var settings = await admin.GetSettingsAsync();
            GstRateInput.Text = settings.GstRate.ToString("0.##", CultureInfo.CurrentCulture);
            configuredReceiptPaperWidth = settings.ReceiptPaperWidthMm == 58 ? ReceiptPaperWidth.Mm58 : ReceiptPaperWidth.Mm80;
            ReceiptPaperSelector.SelectedIndex = configuredReceiptPaperWidth == ReceiptPaperWidth.Mm58 ? 0 : 1;
            ReceiptPaperStatusText.Text = $"Saved setting: {(int)configuredReceiptPaperWidth} mm";
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
    private async void ApplyReceiptPaperWidth_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var paperWidth = ReceiptPaperSelector.SelectedIndex == 0 ? ReceiptPaperWidth.Mm58 : ReceiptPaperWidth.Mm80;
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IAdministrationService>().UpdateReceiptPaperWidthAsync(paperWidth, session.CurrentUser!.Id);
            configuredReceiptPaperWidth = paperWidth;
            ReceiptPaperStatusText.Text = $"Saved setting: {(int)paperWidth} mm";
            AdminStatusText.Text = "Physical receipt paper setting updated.";
        }
        catch (Exception ex) { ReceiptPaperStatusText.Text = ex.Message; }
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
        lastPaidOrder = paidOrder;
        LastReceiptText.Text = $"{paidOrder.InvoiceNumber} • {paidOrder.GrandTotal:N2}";
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
        var confirmation = new OrderCancellationDialog($"You are about to cancel {context}.") { Owner = this };
        if (confirmation.ShowDialog() != true) return;
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
    private ReceiptPaperWidth SelectedReceiptPaperWidth => configuredReceiptPaperWidth;
    private async Task<string> PrintReceiptAsync(Order order, bool isReprint)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var printed = await scope.ServiceProvider.GetRequiredService<IReceiptPrinter>().PrintAsync(order, isReprint, SelectedReceiptPaperWidth);
            if (!printed) { StatusText.Text = "Payment completed; printing was cancelled."; return "Payment completed; printing was cancelled."; }
            invoicePrintedForCurrentOrder = true;
            ReprintButton.Visibility = Visibility.Visible;
            StatusText.Text = isReprint ? "Invoice sent for reprint." : "Invoice sent to the printer.";
            return StatusText.Text;
        }
        catch (Exception ex) { StatusText.Text = $"Payment completed; invoice printing failed: {ex.Message}"; return StatusText.Text; }
    }
    private async void ExportLastReceiptPdf_Click(object sender, RoutedEventArgs e)
    {
        if (lastPaidOrder is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Export compact receipt PDF", Filter = "PDF document (*.pdf)|*.pdf", DefaultExt = ".pdf", AddExtension = true, FileName = $"{lastPaidOrder.InvoiceNumber}.pdf" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IReceiptPrinter>().ExportPdfAsync(lastPaidOrder, false, dialog.FileName, SelectedReceiptPaperWidth);
            HomeStaffText.Text = $"Compact receipt PDF saved to {dialog.FileName}. Signed in as {session.CurrentUser!.DisplayName} ({session.CurrentUser.Role}).";
        }
        catch (Exception ex) { HomeStaffText.Text = $"PDF export failed: {ex.Message}"; }
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
        if (serverNameEditorRow is not null) serverNameEditorRow.Visibility = pendingOrderType is not null || currentOrder is not null ? Visibility.Visible : Visibility.Collapsed;
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
