using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application;
using RestaurantPos.Domain;

namespace RestaurantPos.Desktop;

public partial class InteractiveFloorPlanEditorView : UserControl
{
    internal const double CellWidth = 90;
    internal const double CellHeight = 72;
    private const int PlanColumns = 12;
    private const int PlanRows = 9;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly UserSession session;
    private readonly Dictionary<int, Border> tableSurfaces = [];
    private List<FloorLayout> layouts = [];
    private DiningTable? selectedTable;
    private bool isReloading;
    private Point dragStart;
    private Point dragOrigin;

    public event EventHandler? DoneRequested;

    public InteractiveFloorPlanEditorView(IServiceScopeFactory scopeFactory, UserSession session)
    {
        InitializeComponent();
        this.scopeFactory = scopeFactory;
        this.session = session;
        ShapeSelector.ItemsSource = Enum.GetValues<TableShape>();
        ShapeSelector.SelectedIndex = 0;
        SelectedShape.ItemsSource = Enum.GetValues<TableShape>();
    }

    private FloorLayout? SelectedLayout => LayoutSelector.SelectedItem as FloorLayout;

    public async Task ReloadAsync(int? selectedId = null, int? selectedTableId = null)
    {
        using var scope = scopeFactory.CreateScope();
        layouts = (await scope.ServiceProvider.GetRequiredService<IFloorPlanService>().GetLayoutsAsync()).ToList();
        isReloading = true;
        LayoutSelector.ItemsSource = layouts;
        LayoutSelector.SelectedItem = layouts.FirstOrDefault(x => x.Id == selectedId) ?? layouts.FirstOrDefault();
        isReloading = false;
        RefreshSelection(selectedTableId);
    }

    private void LayoutChanged(object sender, SelectionChangedEventArgs e) { if (!isReloading) RefreshSelection(); }

    private void RefreshSelection(int? selectedTableId = null)
    {
        var layout = SelectedLayout;
        var sections = layout?.Sections.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToList() ?? [];
        SectionSelector.ItemsSource = sections;
        SectionSelector.SelectedIndex = -1;
        SelectedSection.ItemsSource = sections;
        selectedTable = layout?.Tables.FirstOrDefault(x => x.Id == selectedTableId && x.IsActive);
        RenderPlan();
        PopulateSelectedTable();
    }

    private void RenderPlan()
    {
        PlanCanvas.Children.Clear();
        tableSurfaces.Clear();
        var tables = SelectedLayout?.Tables.Where(x => x.IsActive).OrderBy(x => x.GridY).ThenBy(x => x.GridX) ?? Enumerable.Empty<DiningTable>();
        foreach (var table in tables)
        {
            var visual = CreateTableVisual(table);
            Canvas.SetLeft(visual, table.GridX * CellWidth);
            Canvas.SetTop(visual, table.GridY * CellHeight);
            PlanCanvas.Children.Add(visual);
        }
    }

    private FrameworkElement CreateTableVisual(DiningTable table)
    {
        var wrapper = new Grid { Width = Math.Max(CellWidth, table.GridWidth * CellWidth), Height = Math.Max(CellHeight, table.GridHeight * CellHeight), Tag = table };
        var sectionName = SelectedLayout?.Sections.FirstOrDefault(x => x.Id == table.FloorSectionId)?.Name;
        var details = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(new TextBlock { Text = table.Name, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1E3A8A"), TextAlignment = TextAlignment.Center });
        details.Children.Add(new TextBlock { Text = $"{table.Capacity} seats", FontSize = 11, Foreground = Brush("#475569"), TextAlignment = TextAlignment.Center });
        details.Children.Add(new TextBlock { Text = sectionName ?? "No section", FontSize = 10, Foreground = Brush("#64748B"), TextAlignment = TextAlignment.Center });
        var surface = new Border { Margin = new Thickness(5), Padding = new Thickness(8), Background = Brush("#EFF6FF"), BorderBrush = Brush("#93C5FD"), BorderThickness = new Thickness(1), CornerRadius = table.Shape == TableShape.Round ? new CornerRadius(999) : new CornerRadius(9), Cursor = Cursors.SizeAll, Child = details };
        surface.MouseLeftButtonDown += TableSurface_MouseLeftButtonDown;
        surface.MouseMove += TableSurface_MouseMove;
        surface.MouseLeftButtonUp += TableSurface_MouseLeftButtonUp;
        tableSurfaces[table.Id] = surface;
        wrapper.Children.Add(surface);
        var resize = new Thumb { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 1, 1), Cursor = Cursors.SizeNWSE, Background = Brush("#2563EB"), BorderBrush = Brushes.White, BorderThickness = new Thickness(2), Tag = table, ToolTip = "Drag to resize" };
        resize.DragStarted += Resize_DragStarted;
        resize.DragDelta += Resize_DragDelta;
        resize.DragCompleted += Resize_DragCompleted;
        wrapper.Children.Add(resize);
        return wrapper;
    }

    private void TableSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Parent: Grid wrapper } surface || wrapper.Tag is not DiningTable table) return;
        SelectTable(table);
        dragStart = e.GetPosition(PlanCanvas);
        dragOrigin = new Point(Canvas.GetLeft(wrapper), Canvas.GetTop(wrapper));
        surface.CaptureMouse();
        e.Handled = true;
    }

    private void TableSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Border { IsMouseCaptured: true, Parent: Grid wrapper }) return;
        var point = e.GetPosition(PlanCanvas);
        Canvas.SetLeft(wrapper, Math.Clamp(dragOrigin.X + point.X - dragStart.X, 0, PlanCanvas.Width - wrapper.Width));
        Canvas.SetTop(wrapper, Math.Clamp(dragOrigin.Y + point.Y - dragStart.Y, 0, PlanCanvas.Height - wrapper.Height));
    }

    private async void TableSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { IsMouseCaptured: true, Parent: Grid wrapper } surface || wrapper.Tag is not DiningTable table) return;
        surface.ReleaseMouseCapture();
        table.GridX = SnapPosition(Canvas.GetLeft(wrapper), CellWidth, PlanColumns - table.GridWidth);
        table.GridY = SnapPosition(Canvas.GetTop(wrapper), CellHeight, PlanRows - table.GridHeight);
        Canvas.SetLeft(wrapper, table.GridX * CellWidth);
        Canvas.SetTop(wrapper, table.GridY * CellHeight);
        await SaveVisualChangeAsync(table, "Table position saved.");
    }

    private void Resize_DragStarted(object sender, DragStartedEventArgs e) { if (sender is Thumb { Tag: DiningTable table }) SelectTable(table); }

    private void Resize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Parent: Grid wrapper }) return;
        wrapper.Width = Math.Clamp(wrapper.Width + e.HorizontalChange, CellWidth, PlanCanvas.Width - Canvas.GetLeft(wrapper));
        wrapper.Height = Math.Clamp(wrapper.Height + e.VerticalChange, CellHeight, PlanCanvas.Height - Canvas.GetTop(wrapper));
    }

    private async void Resize_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (sender is not Thumb { Parent: Grid wrapper, Tag: DiningTable table }) return;
        table.GridWidth = Math.Clamp((int)Math.Round(wrapper.Width / CellWidth), 1, PlanColumns - table.GridX);
        table.GridHeight = Math.Clamp((int)Math.Round(wrapper.Height / CellHeight), 1, PlanRows - table.GridY);
        wrapper.Width = table.GridWidth * CellWidth;
        wrapper.Height = table.GridHeight * CellHeight;
        await SaveVisualChangeAsync(table, "Table size saved.");
    }

    private void SelectTable(DiningTable table)
    {
        selectedTable = table;
        foreach (var pair in tableSurfaces)
        {
            pair.Value.BorderBrush = Brush(pair.Key == table.Id ? "#2563EB" : "#93C5FD");
            pair.Value.BorderThickness = new Thickness(pair.Key == table.Id ? 3 : 1);
        }
        PopulateSelectedTable();
    }

    private void PopulateSelectedTable()
    {
        SelectedTablePanel.IsEnabled = selectedTable is not null;
        SelectionHint.Text = selectedTable is null ? "Select a table on the grid." : $"Editing {selectedTable.Name} • column {selectedTable.GridX + 1}, row {selectedTable.GridY + 1} • {selectedTable.GridWidth} × {selectedTable.GridHeight}";
        if (selectedTable is null) return;
        SelectedTableName.Text = selectedTable.Name;
        SelectedCapacity.Text = selectedTable.Capacity.ToString();
        SelectedSection.SelectedItem = (SelectedSection.ItemsSource as IEnumerable<FloorSection>)?.FirstOrDefault(x => x.Id == selectedTable.FloorSectionId);
        SelectedShape.SelectedItem = selectedTable.Shape;
    }

    private Task SaveVisualChangeAsync(DiningTable table, string message) => RunAsync(service => service.UpdateTableAsync(table.Id, table.Name, table.Capacity, table.GridX, table.GridY, table.GridWidth, table.GridHeight, table.Shape, table.FloorSectionId, true, session.CurrentUser!.Id), message, table.Id);

    private async void AddLayout_Click(object sender, RoutedEventArgs e) => await RunAsync(async service => { var layout = await service.AddLayoutAsync(NewLayoutName.Text, session.CurrentUser!.Id); NewLayoutName.Clear(); await ReloadAsync(layout.Id); }, "Floor added.", reload: false);

    private async void AddSection_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLayout is null) { EditorStatusText.Text = "Select a floor first."; return; }
        await RunAsync(async service => { await service.AddSectionAsync(SelectedLayout.Id, NewSectionName.Text, session.CurrentUser!.Id); NewSectionName.Clear(); }, "Section added.");
    }

    private async void AddTable_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLayout is null || !int.TryParse(CapacityInput.Text, out var capacity) || ShapeSelector.SelectedItem is not TableShape shape) { EditorStatusText.Text = "Choose a floor and enter valid table details."; return; }
        var layoutId = SelectedLayout.Id;
        var position = FindNextPosition(SelectedLayout.Tables.Where(x => x.IsActive));
        await RunAsync(async service => { var table = await service.AddTableAsync(layoutId, (SectionSelector.SelectedItem as FloorSection)?.Id, TableName.Text, capacity, position.X, position.Y, shape, session.CurrentUser!.Id); TableName.Clear(); await ReloadAsync(layoutId, table.Id); }, "Table added to the first available grid position.", reload: false);
    }

    private async void SaveSelectedTable_Click(object sender, RoutedEventArgs e)
    {
        if (selectedTable is null || !int.TryParse(SelectedCapacity.Text, out var capacity) || SelectedShape.SelectedItem is not TableShape shape) { EditorStatusText.Text = "Enter valid table details."; return; }
        var table = selectedTable;
        await RunAsync(service => service.UpdateTableAsync(table.Id, SelectedTableName.Text, capacity, table.GridX, table.GridY, table.GridWidth, table.GridHeight, shape, (SelectedSection.SelectedItem as FloorSection)?.Id, true, session.CurrentUser!.Id), "Table details saved.", table.Id);
    }

    private async void DeactivateSelectedTable_Click(object sender, RoutedEventArgs e)
    {
        if (selectedTable is null || MessageBox.Show($"Remove {selectedTable.Name} from this floor plan? Historical orders will be kept.", "Remove table", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var table = selectedTable;
        await RunAsync(service => service.UpdateTableAsync(table.Id, table.Name, table.Capacity, table.GridX, table.GridY, table.GridWidth, table.GridHeight, table.Shape, table.FloorSectionId, false, session.CurrentUser!.Id), "Table removed from the active floor plan.");
    }

    private async Task RunAsync(Func<IFloorPlanService, Task> action, string success, int? selectedTableId = null, bool reload = true)
    {
        var layoutId = SelectedLayout?.Id;
        try { using var scope = scopeFactory.CreateScope(); await action(scope.ServiceProvider.GetRequiredService<IFloorPlanService>()); if (reload) await ReloadAsync(layoutId, selectedTableId); EditorStatusText.Text = success; }
        catch (Exception ex) { EditorStatusText.Text = ex.Message; await ReloadAsync(layoutId, selectedTable?.Id); }
    }

    internal static int SnapPosition(double position, double cellSize, int maximum) => Math.Clamp((int)Math.Round(position / cellSize), 0, Math.Max(0, maximum));
    internal static (int X, int Y) FindNextPosition(IEnumerable<DiningTable> tables)
    {
        var occupied = tables.SelectMany(table => Enumerable.Range(table.GridX, table.GridWidth).SelectMany(x => Enumerable.Range(table.GridY, table.GridHeight).Select(y => (x, y)))).ToHashSet();
        for (var y = 0; y < PlanRows; y++) for (var x = 0; x < PlanColumns; x++) if (!occupied.Contains((x, y))) return (x, y);
        return (0, 0);
    }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private void Done_Click(object sender, RoutedEventArgs e) => DoneRequested?.Invoke(this, EventArgs.Empty);
}
