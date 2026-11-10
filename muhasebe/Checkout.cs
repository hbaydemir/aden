using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Aden;

public sealed partial class MainWindow
{
    private Action? refreshCatalog;
    private TextBox? saleSearch;

    private StackPanel CheckoutLayout()
    {
        var layout = new Grid { Margin = new Thickness(3, 0, 3, 0) };
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(355) });
        tabs.Items.Add(new TabItem { Header = "Hızlı satış", Content = layout });

        var catalog = new DockPanel();
        var catalogCard = Surface(catalog, new Thickness(14));
        catalogCard.Margin = new Thickness(0, 0, 13, 0);
        layout.Children.Add(catalogCard);
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        catalog.Children.Add(top);
        top.Children.Add(Label("Ürün kataloğu", 20, bold: true));
        var help = Label("Barkodu okut veya bir ürüne dokun.", 12, "#536580");
        help.Margin = new Thickness(0, 4, 0, 8);
        top.Children.Add(help);
        var query = Form(top);
        var search = Input(query, "Barkod, ürün adı veya kod", width: 165);
        saleSearch = search;
        var count = Input(query, "Adet", "1", 53);

        void AddProduct(Product product)
        {
            if (payments.Count > 0)
                throw new InvalidOperationException("Sepeti değiştirmek için önce kayıtlı ödeme parçalarını kaldır.");
            var quantity = Quantity(count);
            var existing = basket.FirstOrDefault(line => line.Code == product.Code && line.UnitPrice == product.Price);
            if (existing is null)
                basket.Add(new(product.Code, product.Name, quantity, product.Price));
            else
            {
                if ((long)existing.Quantity + quantity > 1_000_000)
                    throw new InvalidOperationException("Adet sınırı aşıldı.");
                basket[basket.IndexOf(existing)] = existing with { Quantity = existing.Quantity + quantity };
            }
            search.Clear();
            count.Text = "1";
            Totals();
            search.Focus();
        }

        var addRow = Form(top);
        Button(addRow, "Barkod / kod ile ekle", () => AddProduct(Find(Required(search))));
        var result = Label("", 12, "#536580", true);
        result.Margin = new Thickness(4, 7, 4, 4);
        top.Children.Add(result);
        var tiles = new WrapPanel();
        var catalogBody = new StackPanel();
        catalogBody.Children.Add(tiles);
        var pages = Form(catalogBody);
        var scroll = TouchScroll(catalogBody);
        catalog.Children.Add(scroll);
        var page = 0;
        const int pageSize = 24;
        void RenderCatalog()
        {
            tiles.Children.Clear();
            pages.Children.Clear();
            var term = search.Text.Trim();
            var matched = products.Values
                .Where(product => term.Length == 0 || product.Barcode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    product.Code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    culture.CompareInfo.IndexOf(product.Name, term, CompareOptions.IgnoreCase) >= 0)
                .OrderBy(product => product.Name, StringComparer.Create(culture, true)).ToArray();
            var lastPage = Math.Max(0, (matched.Length - 1) / pageSize);
            page = Math.Clamp(page, 0, lastPage);
            result.Text = matched.Length == 0 ? "Ürün bulunamadı" : $"{matched.Length} ürün · Sayfa {page + 1}/{lastPage + 1}";
            if (matched.Length == 0 && products.Count == 0)
            {
                var empty = new StackPanel { Margin = new Thickness(4, 16, 4, 4) };
                empty.Children.Add(Label("Kataloğun hazır", 19, bold: true));
                empty.Children.Add(Label("Satışa başlamak için ilk ürün kartını ekle.", 13, "#536580"));
                Button(empty, "Ürünlere git", () => tabs.SelectedIndex = 0).HorizontalAlignment = HorizontalAlignment.Left;
                tiles.Children.Add(empty);
            }
            foreach (var product in matched.Skip(page * pageSize).Take(pageSize))
            {
                var available = stock.GetValueOrDefault(product.Code).Shop;
                var tile = Button(tiles, product.Name, () => AddProduct(product));
                tile.Background = Paint(available > 0 ? "#F7FAFF" : "#F7F8FB");
                tile.Width = TileWidth();
                tile.Height = 111;
                tile.Padding = new Thickness(11, 9, 11, 9);
                tile.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                tile.VerticalContentAlignment = VerticalAlignment.Stretch;
                var card = new DockPanel();
                var price = Label(Format(product.Price), 17, "#1D40AF", true);
                DockPanel.SetDock(price, Dock.Bottom);
                card.Children.Add(price);
                var countLabel = Label($"{available} adet · {product.Code}", 11, "#536580");
                countLabel.TextTrimming = TextTrimming.CharacterEllipsis;
                DockPanel.SetDock(countLabel, Dock.Bottom);
                card.Children.Add(countLabel);
                var name = Label(product.Name, 14, bold: true);
                name.TextWrapping = TextWrapping.Wrap;
                name.MaxHeight = 40;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                card.Children.Add(name);
                tile.Content = card;
                AutomationProperties.SetName(tile, $"{product.Name}, {Format(product.Price)}, dükkânda {available} adet");
            }
            if (page > 0) Button(pages, "Önceki", () => { page--; RenderCatalog(); });
            if (page < lastPage) Button(pages, "Sonraki", () => { page++; RenderCatalog(); });
        }
        double TileWidth()
        {
            var width = scroll.ViewportWidth;
            if (width <= 0) return 142;
            var columns = Math.Max(1, (int)(width / 114));
            return Math.Max(106, Math.Floor(width / columns) - 9);
        }

        scroll.SizeChanged += (_, _) =>
        {
            foreach (var tile in tiles.Children.OfType<Button>()) tile.Width = TileWidth();
        };
        refreshCatalog = RenderCatalog;
        search.TextChanged += (_, _) => { page = 0; RenderCatalog(); };
        search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            try { AddProduct(Find(Required(search))); }
            catch (Exception error) { MessageBox.Show(error.Message, "Aden"); }
        };

        var checkout = new DockPanel();
        var checkoutCard = Surface(checkout, new Thickness(12));
        System.Windows.Controls.Grid.SetColumn(checkoutCard, 1);
        layout.Children.Add(checkoutCard);
        var header = Label("Satış sepeti", 20, bold: true);
        header.Margin = new Thickness(4, 0, 4, 4);
        DockPanel.SetDock(header, Dock.Top);
        checkout.Children.Add(header);

        var footer = new StackPanel();
        footer.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(footer, Dock.Bottom);
        checkout.Children.Add(footer);
        checkoutFooter = footer;

        var details = new StackPanel();
        checkout.Children.Add(TouchScroll(details));
        return details;
    }

    private StackPanel checkoutFooter = null!;

    private void Columns(DataGrid grid, params (string Heading, string Path, double Weight)[] columns)
    {
        grid.AutoGenerateColumns = false;
        foreach (var column in columns)
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Heading, Binding = new Binding(column.Path)
                {
                    Converter = column.Path is "UnitPrice" or "Amount" ? new MoneyConverter(culture) : null
                },
                Width = new DataGridLength(column.Weight, DataGridLengthUnitType.Star)
            });
    }

    private sealed class MoneyConverter(CultureInfo culture) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo language) =>
            value is long cents ? (cents / 100m).ToString("C2", culture) : "";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo language) =>
            Binding.DoNothing;
    }
}


