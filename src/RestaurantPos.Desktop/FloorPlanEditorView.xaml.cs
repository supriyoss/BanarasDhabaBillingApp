using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application;
using RestaurantPos.Domain;

namespace RestaurantPos.Desktop;

public partial class FloorPlanEditorView : UserControl
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly UserSession session;
    private List<FloorLayout> layouts = [];

    public event EventHandler? DoneRequested;

    public FloorPlanEditorView(IServiceScopeFactory scopeFactory, UserSession session)
    {
        InitializeComponent();
        this.scopeFactory = scopeFactory;
        this.session = session;
        ShapeSelector.ItemsSource = Enum.GetValues<TableShape>();
        ShapeSelector.SelectedIndex = 0;
    }

    private FloorLayout? SelectedLayout => LayoutSelector.SelectedItem as FloorLayout;

    public async Task ReloadAsync(int? selectedId = null)
    {
        using var scope = scopeFactory.CreateScope();
        layouts = (await scope.ServiceProvider.GetRequiredService<IFloorPlanService>().GetLayoutsAsync()).ToList();
        LayoutSelector.ItemsSource = layouts;
        LayoutSelector.SelectedItem = layouts.FirstOrDefault(x => x.Id == selectedId) ?? layouts.FirstOrDefault();
        RefreshSelection();
    }

    private void LayoutChanged(object sender, SelectionChangedEventArgs e) => RefreshSelection();

    private void RefreshSelection()
    {
        var layout = SelectedLayout;
        SectionSelector.ItemsSource = layout?.Sections.OrderBy(x => x.SortOrder).ToList();
        SectionSelector.SelectedIndex = -1;
        TablesGrid.ItemsSource = layout?.Tables.OrderBy(x => x.GridY).ThenBy(x => x.GridX).ToList();
    }

    private async void AddLayout_Click(object sender, RoutedEventArgs e) => await RunAsync(async service =>
    {
        var layout = await service.AddLayoutAsync(NewLayoutName.Text, session.CurrentUser!.Id);
        NewLayoutName.Clear();
        await ReloadAsync(layout.Id);
    }, "Floor added.");

    private async void AddSection_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLayout is null) { EditorStatusText.Text = "Select a floor first."; return; }
        var layoutId = SelectedLayout.Id;
        await RunAsync(async service =>
        {
            await service.AddSectionAsync(layoutId, NewSectionName.Text, session.CurrentUser!.Id);
            NewSectionName.Clear();
            await ReloadAsync(layoutId);
        }, "Section added.");
    }

    private async void AddTable_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLayout is null || !int.TryParse(CapacityInput.Text, out var capacity) || !int.TryParse(GridXInput.Text, out var x) || !int.TryParse(GridYInput.Text, out var y) || ShapeSelector.SelectedItem is not TableShape shape)
        {
            EditorStatusText.Text = "Choose a floor and enter valid table details.";
            return;
        }

        var layoutId = SelectedLayout.Id;
        var sectionId = (SectionSelector.SelectedItem as FloorSection)?.Id;
        await RunAsync(async service =>
        {
            await service.AddTableAsync(layoutId, sectionId, TableName.Text, capacity, x, y, shape, session.CurrentUser!.Id);
            TableName.Clear();
            await ReloadAsync(layoutId);
        }, "Table added.");
    }

    private async Task RunAsync(Func<IFloorPlanService, Task> action, string success)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<IFloorPlanService>());
            EditorStatusText.Text = success;
        }
        catch (Exception ex)
        {
            EditorStatusText.Text = ex.Message;
        }
    }

    private void Done_Click(object sender, RoutedEventArgs e) => DoneRequested?.Invoke(this, EventArgs.Empty);
}
