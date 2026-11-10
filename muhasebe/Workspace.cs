using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Aden;

public sealed partial class MainWindow
{
    private static SolidColorBrush Paint(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private static TextBlock Label(string text, double size, string color = "#142446", bool bold = false) =>
        new()
        {
            Text = text, FontSize = size, Foreground = Paint(color),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
        };

    private static Border Surface(UIElement child, Thickness padding) =>
        new()
        {
            Child = child, Padding = padding, Background = Brushes.White,
            BorderBrush = Paint("#DCE5F3"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16)
        };

    private void BuildWorkspace()
    {
        Icon = Brand.Mark;
        Width = 1370;
        Height = 880;
        MinWidth = 920;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Paint("#F3F6FC");

        var workspace = new Grid { Background = Background };
        Foreground = Paint("#142446");
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        Content = workspace;

        var rail = new DockPanel { Margin = new Thickness(14, 19, 14, 12) };
        var sidebar = new Border
        {
            Child = rail, Background = Brushes.White,
            BorderThickness = new Thickness(0, 0, 1, 0), BorderBrush = Paint("#DCE5F3")
        };
        workspace.Children.Add(sidebar);
        var brand = new Image
        {
            Source = Brand.Wordmark, Width = 131, Height = 44, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(10, 4, 0, 24)
        };
        DockPanel.SetDock(brand, Dock.Top);
        rail.Children.Add(brand);
        var railTitle = Label("ÇALIŞMA ALANI", 10, "#536580", true);
        railTitle.Margin = new Thickness(10, 0, 0, 8);
        DockPanel.SetDock(railTitle, Dock.Top);
        rail.Children.Add(railTitle);

        var navigation = new ListBox
        {
            BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            ItemContainerStyle = (Style)FindResource("NavItem")
        };
        ScrollViewer.SetPanningMode(navigation, PanningMode.VerticalOnly);
        ScrollViewer.SetHorizontalScrollBarVisibility(navigation, ScrollBarVisibility.Disabled);
        string[] glyphs = ["\uE7B8", "\uE7B8", "\uE7BF", "\uE81C", "\uE8A5",
            "\uE716", "\uE8C7", "\uE8B7", "\uE9D2", "\uE713"];
        for (var i = 0; i < tabs.Items.Count; i++)
        {
            var item = (TabItem)tabs.Items[i];
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = glyphs[i], Width = 25, FontSize = 16, FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Foreground = Paint("#1D40AF"), VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = item.Header.ToString(), FontSize = 13, VerticalAlignment = VerticalAlignment.Center
            });
            navigation.Items.Add(row);
        }
        rail.Children.Add(navigation);

        var body = new DockPanel { Margin = new Thickness(23, 17, 23, 9) };
        System.Windows.Controls.Grid.SetColumn(body, 1);
        workspace.Children.Add(body);
        var top = new DockPanel { Margin = new Thickness(3, 0, 3, 13) };
        var badge = new Border
        {
            Background = Paint("#E9EFFF"), CornerRadius = new CornerRadius(10),
            Child = Label("ADEN  /  MASAÜSTÜ", 10, "#1D40AF", true),
            Padding = new Thickness(10, 7, 10, 7), VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(badge, Dock.Right);
        top.Children.Add(badge);
        if (updateNotice is not null)
        {
            DockPanel.SetDock(updateNotice, Dock.Right);
            top.Children.Add(updateNotice);
        }
        var heading = Label("", 31, bold: true);
        top.Children.Add(heading);
        DockPanel.SetDock(top, Dock.Top);
        body.Children.Add(top);
        status.FontSize = 12;
        status.Foreground = Paint("#536580");
        status.Margin = new Thickness(3, 9, 3, 2);
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        DockPanel.SetDock(status, Dock.Bottom);
        body.Children.Add(status);
        tabs.Style = (Style)FindResource("WorkspaceTabs");
        body.Children.Add(tabs);

        navigation.SelectionChanged += (_, _) =>
        {
            if (navigation.SelectedIndex >= 0) tabs.SelectedIndex = navigation.SelectedIndex;
        };
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != tabs || tabs.SelectedItem is not TabItem selected) return;
            navigation.SelectedIndex = tabs.SelectedIndex;
            heading.Text = selected.Header.ToString();
        };
        tabs.SelectedIndex = 2;
        navigation.SelectedIndex = 2;
        heading.Text = ((TabItem)tabs.Items[2]).Header.ToString();
    }

    private static ScrollViewer TouchScroll(UIElement child) =>
        new() { Content = child, PanningMode = PanningMode.VerticalOnly, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
}


