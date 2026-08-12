using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application;

namespace RestaurantPos.Desktop;

public partial class FloorPlanWindow : Window
{
    private readonly IServiceScopeFactory scopeFactory; private IReadOnlyList<FloorPlanView> layouts = [];
    public int? SelectedTableId { get; private set; }
    public FloorPlanWindow(IServiceScopeFactory scopeFactory) { InitializeComponent(); this.scopeFactory = scopeFactory; Loaded += LoadAsync; }
    private async void LoadAsync(object sender, RoutedEventArgs e) { using var scope = scopeFactory.CreateScope(); layouts = await scope.ServiceProvider.GetRequiredService<IFloorPlanService>().GetLiveFloorPlansAsync(); LayoutSelector.ItemsSource = layouts; LayoutSelector.SelectedItem = layouts.FirstOrDefault(); Render(); }
    private void LayoutChanged(object sender, SelectionChangedEventArgs e) => Render();
    private void Render()
    {
        FloorGrid.Children.Clear(); FloorGrid.RowDefinitions.Clear(); FloorGrid.ColumnDefinitions.Clear();
        if (LayoutSelector.SelectedItem is not FloorPlanView layout) return;
        var columns = Math.Max(8, layout.Tables.Select(x => x.GridX + x.GridWidth).DefaultIfEmpty(8).Max()); var rows = Math.Max(5, layout.Tables.Select(x => x.GridY + x.GridHeight).DefaultIfEmpty(5).Max());
        for (var i = 0; i < columns; i++) FloorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        for (var i = 0; i < rows; i++) FloorGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(110) });
        foreach (var table in layout.Tables)
        {
            var button = new Button { Tag = table.Id, Margin = new Thickness(8), Background = StateBrush(table.State), Content = new TextBlock { Text = table.State == "Available" ? $"{table.Name}\n{table.Capacity} seats\nAvailable" : $"{table.Name}\n{table.State}\n{table.RunningTotal:N2}", TextAlignment = TextAlignment.Center }, ToolTip = table.Section is null ? table.ServerName : $"{table.Section} • {table.ServerName}" };
            button.Click += Table_Click; Grid.SetColumn(button, table.GridX); Grid.SetRow(button, table.GridY); Grid.SetColumnSpan(button, table.GridWidth); Grid.SetRowSpan(button, table.GridHeight); FloorGrid.Children.Add(button);
        }
    }
    private static Brush StateBrush(string state) => state switch { "Occupied" => new SolidColorBrush(Color.FromRgb(186, 230, 253)), "Bill requested" => new SolidColorBrush(Color.FromRgb(254, 240, 138)), "Held" => new SolidColorBrush(Color.FromRgb(254, 240, 138)), _ => new SolidColorBrush(Color.FromRgb(241, 245, 249)) };
    private void Table_Click(object sender, RoutedEventArgs e) { SelectedTableId = (int)((Button)sender).Tag; DialogResult = true; }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
