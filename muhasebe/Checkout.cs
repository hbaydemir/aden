using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Aden;

public sealed partial class MainWindow
{
    private Action? refreshCatalog;
    private Action? refreshCart;
    private TextBox? saleSearch;
    private string saleAccount = "";

    private void Totals() => refreshCart?.Invoke();

    private void AddSales()
    {
        var layout = new Grid { Margin = new Thickness(Gap) };
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(410) });
        posPage = AddTab("Hızlı satış", "", layout);

        // ── Sol: arama ve ürün ızgarası ──
        var catalog = new DockPanel();
        layout.Children.Add(catalog);

        var top = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        top.ColumnDefinitions.Add(new ColumnDefinition());
        for (var c = 0; c < 4; c++) top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        DockPanel.SetDock(top, Dock.Top);
        catalog.Children.Add(top);
        var searchHost = SearchField("Barkod okut, ürün adı veya kodu yaz", out var search, large: true);
        saleSearch = search;
        scanTargets[posPage] = search;
        top.Children.Add(searchHost);

        var countHost = new Grid { Width = 92, Margin = new Thickness(12, 0, 0, 0) };
        var count = new TextBox
        {
            Text = "1", MinHeight = 52, FontSize = 17, Padding = new Thickness(34, 0, 10, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        AutomationProperties.SetName(count, "Adet çarpanı");
        count.ToolTip = "Eklenecek adet";
        countHost.Children.Add(count);
        var times = Label("×", 18, Muted);
        times.Margin = new Thickness(14, 0, 0, 0);
        times.VerticalAlignment = VerticalAlignment.Center;
        times.IsHitTestVisible = false;
        countHost.Children.Add(times);
        Grid.SetColumn(countHost, 1);
        top.Children.Add(countHost);

        var lowOnly = false;
        var lowButton = new Button
        {
            Style = (Style)FindResource("DangerButton"), MinHeight = 52, Padding = new Thickness(Gap, 0, Gap, 0),
            Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed,
            ToolTip = "Dükkân stoku raf alt sınırında veya altında olan ürünler"
        };
        Grid.SetColumn(lowButton, 2);
        top.Children.Add(lowButton);

        var history = Act("Satış geçmişi", ShowSalesHistory, glyph: "");
        history.MinHeight = 52;
        history.Padding = new Thickness(Gap, 0, Gap, 0);
        history.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(history, 3);
        top.Children.Add(history);

        var expand = new Button { MinHeight = 52, Padding = new Thickness(Gap, 0, Gap, 0), Margin = new Thickness(12, 0, 0, 0) };
        expand.Click += (_, _) => SetFullscreen(!fullscreen);
        fullscreenChanged = () => SetButtonContent(expand, fullscreen ? "Küçült" : "Tam ekran", fullscreen ? "" : "");
        fullscreenChanged();
        expand.ToolTip = "Tam ekran (F11)";
        Grid.SetColumn(expand, 4);
        top.Children.Add(expand);

        // Filtreler: Tümü / Barkodsuz, kategori ve sıralama
        var filters = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(filters, Dock.Top);
        catalog.Children.Add(filters);
        var mainFilter = new StackPanel { Orientation = Orientation.Horizontal };
        var filterBar = new Border
        {
            Child = mainFilter, Background = Paint("#E9EBF0"), CornerRadius = new CornerRadius(10), Padding = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(filterBar, Dock.Left);
        filters.Children.Add(filterBar);
        var barcodeless = false;
        var allButton = new Button { Content = "Tümü", Padding = new Thickness(14, 0, 14, 0) };
        var noBarcodeButton = new Button { Content = "Barkodsuzlar", Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(4, 0, 0, 0) };
        mainFilter.Children.Add(allButton);
        mainFilter.Children.Add(noBarcodeButton);
        var categoryFilter = new ComboBox { DisplayMemberPath = nameof(CategoryChoice.Label), Width = 180, MaxDropDownHeight = 360, Margin = new Thickness(10, 0, 0, 0) };
        AutomationProperties.SetName(categoryFilter, "Kategori filtresi");
        DockPanel.SetDock(categoryFilter, Dock.Left);
        filters.Children.Add(categoryFilter);
        var sorts = new[] { "Ada göre (A–Z)", "Stok: azdan çoka", "Stok: çoktan aza", "Fiyat: azdan çoka", "Fiyat: çoktan aza" };
        var sortBox = new ComboBox { ItemsSource = sorts, SelectedIndex = 0, Width = 170, Margin = new Thickness(10, 0, 0, 0) };
        AutomationProperties.SetName(sortBox, "Sıralama");
        DockPanel.SetDock(sortBox, Dock.Left);
        filters.Children.Add(sortBox);
        var pager = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(pager, Dock.Right);
        filters.Children.Add(pager);
        var result = Label("", 13, Muted);
        result.VerticalAlignment = VerticalAlignment.Center;
        result.HorizontalAlignment = HorizontalAlignment.Right;
        result.Margin = new Thickness(10, 0, 8, 0);
        result.TextTrimming = TextTrimming.CharacterEllipsis;
        filters.Children.Add(result);

        void FillCategories()
        {
            var selected = (categoryFilter.SelectedItem as CategoryChoice)?.Id ?? "";
            var items = new List<CategoryChoice> { new("", "Tüm kategoriler") };
            items.AddRange(CategoryTree().Select(c => new CategoryChoice(c.Category.Id, new string(' ', c.Depth * 3) + c.Category.Name)));
            categoryFilter.ItemsSource = items;
            categoryFilter.SelectedItem = items.FirstOrDefault(i => i.Id.Equals(selected, StringComparison.OrdinalIgnoreCase)) ?? items[0];
        }
        FillCategories();
        refreshers.Add(FillCategories);

        var tiles = new WrapPanel();
        var scroll = TouchScroll(tiles);
        scroll.Margin = new Thickness(0, 0, -12, 0);
        catalog.Children.Add(scroll);

        void AddProduct(Product product)
        {
            if (payments.Count > 0)
                throw new InvalidOperationException("Sepeti değiştirmek için önce kayıtlı ödeme parçalarını kaldır.");
            var quantity = Quantity(count);
            if (OptionsOf(product).Length > 0) PickVariant(product, variant => AddLine(product, quantity, variant));
            else AddLine(product, quantity, null);
        }

        void AddLine(Product product, int quantity, string? variant)
        {
            var priced = LineFor(product, quantity);
            if (variant is not null) priced = priced with { Name = $"{product.Name} · {variant}" };
            var existing = basket.FirstOrDefault(line => line.Code == product.Code && line.Name == priced.Name &&
                line.UnitPrice == priced.UnitPrice && line.Discount == 0);
            if (existing is null)
                basket.Add(priced);
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

        var page = 0;
        const int pageSize = 48;
        const double tileGap = 14;
        const double minTile = 236;
        void RenderCatalog()
        {
            tiles.Children.Clear();
            pager.Children.Clear();
            allButton.Style = (Style)FindResource(barcodeless ? "SegmentButton" : "SegmentActiveButton");
            noBarcodeButton.Style = (Style)FindResource(barcodeless ? "SegmentActiveButton" : "SegmentButton");
            var low = products.Values.Count(IsLow);
            if (low == 0) lowOnly = false;
            lowButton.Visibility = low > 0 ? Visibility.Visible : Visibility.Collapsed;
            SetButtonContent(lowButton, lowOnly ? "Raf sınırı filtresi açık" : $"Raf sınırı · {low}", lowOnly ? "" : "");

            var term = search.Text.Trim();
            var categoryId = (categoryFilter.SelectedItem as CategoryChoice)?.Id ?? "";
            var filtered = products.Values
                .Where(product => !barcodeless || !HasBarcode(product))
                .Where(product => !lowOnly || IsLow(product))
                .Where(product => categoryId.Length == 0 || IsDescendant(CategoryOf(product).Id, categoryId))
                .Where(product => term.Length == 0 ||
                    (HasBarcode(product) && product.Barcode.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    product.Code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    culture.CompareInfo.IndexOf(product.Name, term, CompareOptions.IgnoreCase) >= 0);
            var byName = StringComparer.Create(culture, true);
            var matched = (sortBox.SelectedIndex switch
            {
                1 => filtered.OrderBy(p => stock.GetValueOrDefault(p.Code).Shop).ThenBy(p => p.Name, byName),
                2 => filtered.OrderByDescending(p => stock.GetValueOrDefault(p.Code).Shop).ThenBy(p => p.Name, byName),
                3 => filtered.OrderBy(SaleGross).ThenBy(p => p.Name, byName),
                4 => filtered.OrderByDescending(SaleGross).ThenBy(p => p.Name, byName),
                _ => filtered.OrderBy(p => p.Name, byName)
            }).ToArray();
            var lastPage = Math.Max(0, (matched.Length - 1) / pageSize);
            page = Math.Clamp(page, 0, lastPage);
            result.Text = matched.Length == 0 ? "Ürün bulunamadı" : $"{matched.Length} ürün";
            if (products.Count == 0)
            {
                var empty = new StackPanel { Margin = new Thickness(0, PagePadding, 0, 0) };
                empty.Children.Add(Label("Katalog boş", 18, bold: true));
                var hint = Label("Satışa başlamak için ilk ürün kartını ekle.", 13, Muted);
                hint.Margin = new Thickness(0, 4, 0, Gap);
                empty.Children.Add(hint);
                var go = Act("Ürünlere git", () => SelectPage(productsPage), "PrimaryButton", "");
                go.HorizontalAlignment = HorizontalAlignment.Left;
                empty.Children.Add(go);
                tiles.Children.Add(empty);
            }
            var size = TileWidth();
            foreach (var product in matched.Skip(page * pageSize).Take(pageSize))
                tiles.Children.Add(Tile(product, size));
            if (lastPage > 0)
            {
                var previous = Act("", () => { page--; RenderCatalog(); }, glyph: "");
                previous.IsEnabled = page > 0;
                previous.Width = 40;
                pager.Children.Add(previous);
                var label = Label($"{page + 1} / {lastPage + 1}", 13, Muted, true);
                label.VerticalAlignment = VerticalAlignment.Center;
                Append(pager, label);
                var next = Act("", () => { page++; RenderCatalog(); }, glyph: "");
                next.IsEnabled = page < lastPage;
                next.Width = 40;
                Append(pager, next);
            }
        }

        Border Pill(string text, string background, string foreground, string? glyph = null, HorizontalAlignment h = HorizontalAlignment.Left,
            VerticalAlignment v = VerticalAlignment.Top)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (glyph is not null)
            {
                var icon = Glyph(glyph, 11, foreground);
                icon.Margin = new Thickness(0, 0, 5, 0);
                content.Children.Add(icon);
            }
            content.Children.Add(Label(text, 12, foreground, true));
            return new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 9, 4), Margin = new Thickness(10),
                HorizontalAlignment = h, VerticalAlignment = v, Background = Paint(background), Child = content
            };
        }

        Button Tile(Product product, double width)
        {
            var available = stock.GetValueOrDefault(product.Code).Shop;
            var discounted = product.SalePrice < product.Price;
            var low = IsLow(product);
            var unit = product.Unit.ToLower(culture);
            var tile = new Button
            {
                Style = (Style)FindResource("PosTile"), Width = width, Height = TileHeight(width),
                Margin = new Thickness(0, 0, tileGap, tileGap), Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch
            };
            tile.Click += (_, _) => Run(() => AddProduct(product));
            var card = new Grid();
            card.SizeChanged += (_, _) => card.Clip = new RectangleGeometry(new Rect(card.RenderSize), 13, 13);
            card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Fotoğraf alanı ve rozetler
            var photo = new Grid { Background = Paint("#F1F2F4") };
            photo.Children.Add(new Image { Source = Photo(CoverOf(product), 480), Stretch = Stretch.UniformToFill });
            photo.Children.Add(new Border
            {
                Height = 56, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
                Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0x40, 0, 0, 0), 90)
            });
            photo.Children.Add(available > 0
                ? Pill($"{available} {unit}", "#F2FFFFFF", low ? "#B45309" : "#15803D", "")
                : Pill("Stok yok", "#F2DC2626", "#FFFFFF", ""));
            if (discounted)
                photo.Children.Add(Pill($"−%{DiscountRate(ListNet(product), SaleNet(product)).ToString("0.#", culture)}", "#DC2626", "#FFFFFF",
                    "", HorizontalAlignment.Right));
            if (low && available > 0)
                photo.Children.Add(Pill("Stok azaldı", "#DC2626", "#FFFFFF", "", v: VerticalAlignment.Bottom));
            if (product.Variation.Length > 0 && VariationName(product) is { Length: > 0 } variation)
                photo.Children.Add(Pill($"{OptionsOf(product).Length} {variation.ToLower(culture)}", "#E61E293B", "#FFFFFF", "",
                    HorizontalAlignment.Right, VerticalAlignment.Bottom));
            card.Children.Add(photo);

            var info = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
            Grid.SetRow(info, 1);
            var name = Label(product.Name, 14, bold: true);
            name.TextWrapping = TextWrapping.Wrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Height = 38;
            name.LineHeight = 19;
            name.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            name.ToolTip = product.Name;
            info.Children.Add(name);
            var description = Label(product.Description.Length > 0 ? product.Description.ReplaceLineEndings(" ") : "-", 12, Muted);
            description.TextTrimming = TextTrimming.CharacterEllipsis;
            description.Margin = new Thickness(0, 2, 0, 0);
            if (product.Description.Length > 0) description.ToolTip = product.Description;
            info.Children.Add(description);
            var prices = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = false };
            var price = Label(Format(SaleGross(product)), 18, discounted ? "#DC2626" : "#2563EB", true);
            price.VerticalAlignment = VerticalAlignment.Bottom;
            prices.Children.Add(price);
            if (discounted)
            {
                var old = Label(Format(ListGross(product)), 12, Muted);
                old.TextDecorations = TextDecorations.Strikethrough;
                old.VerticalAlignment = VerticalAlignment.Bottom;
                old.Margin = new Thickness(6, 0, 0, 2);
                prices.Children.Add(old);
            }
            var per = Label($"/ {unit}", 12, Muted);
            per.VerticalAlignment = VerticalAlignment.Bottom;
            per.Margin = new Thickness(0, 0, 0, 2);
            DockPanel.SetDock(per, Dock.Right);
            prices.Children.Add(per);
            info.Children.Add(prices);
            card.Children.Add(info);
            tile.Content = card;
            AutomationProperties.SetName(tile, $"{product.Name}, {Format(SaleGross(product))}, dükkânda {available} {unit}" + (low ? ", stok azaldı" : ""));
            return tile;
        }

        double TileWidth()
        {
            var width = scroll.ViewportWidth;
            if (width <= 0) return minTile;
            var columns = Math.Max(1, (int)(width / (minTile + tileGap)));
            return Math.Floor(width / columns) - tileGap;
        }
        static double TileHeight(double width) => Math.Round(width * 0.6) + 128;

        scroll.ScrollChanged += (_, e) =>
        {
            if (e.ViewportWidthChange == 0) return;
            var size = TileWidth();
            foreach (var tile in tiles.Children.OfType<Button>()) { tile.Width = size; tile.Height = TileHeight(size); }
        };
        refreshCatalog = RenderCatalog;
        allButton.Click += (_, _) => { barcodeless = false; page = 0; RenderCatalog(); };
        noBarcodeButton.Click += (_, _) => { barcodeless = true; page = 0; RenderCatalog(); };
        lowButton.Click += (_, _) => { lowOnly = !lowOnly; page = 0; RenderCatalog(); };
        categoryFilter.SelectionChanged += (_, _) => { page = 0; RenderCatalog(); };
        sortBox.SelectionChanged += (_, _) => { page = 0; RenderCatalog(); };
        search.TextChanged += (_, _) => { page = 0; RenderCatalog(); };
        search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Run(() => AddProduct(Find(Required(search))));
        };

        // ── Sağ: sepet ──
        var cartDock = new DockPanel();
        var cartCard = Card(cartDock);
        Grid.SetColumn(cartCard, 2);
        layout.Children.Add(cartCard);

        var header = new DockPanel { Margin = new Thickness(Gap, Gap, 12, Gap) };
        var clear = Act("", () =>
        {
            if (basket.Count == 0) return;
            Confirm("Sepeti temizle", "Sepetteki tüm ürünler ve ödeme parçaları kaldırılacak.", "Temizle", () =>
            {
                basket.Clear();
                payments.Clear();
                Totals();
            });
        }, "GhostButton", "");
        clear.Width = 40;
        clear.ToolTip = "Sepeti temizle";
        DockPanel.SetDock(clear, Dock.Right);
        header.Children.Add(clear);
        var accountButton = new Button { MinHeight = 36, Margin = new Thickness(0, 0, 4, 0) };
        accountButton.Click += (_, _) => Run(PickAccount);
        DockPanel.SetDock(accountButton, Dock.Right);
        header.Children.Add(accountButton);
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(Label("Sepet", 18, bold: true));
        var countBadge = new Border
        {
            Background = Paint("#EFF4FF"), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(SmallGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        var countText = Label("0", 12, "#2563EB", true);
        countBadge.Child = countText;
        titleRow.Children.Add(countBadge);
        header.Children.Add(titleRow);
        var headerBar = new Border { Child = header, BorderBrush = Paint(LineColor), BorderThickness = new Thickness(0, 0, 0, 1) };
        DockPanel.SetDock(headerBar, Dock.Top);
        cartDock.Children.Add(headerBar);

        var footer = new StackPanel { Margin = new Thickness(Gap) };
        var footerBar = new Border
        {
            Child = footer, Background = Paint("#F9FAFB"), BorderBrush = Paint(LineColor), BorderThickness = new Thickness(0, 1, 0, 0)
        };
        DockPanel.SetDock(footerBar, Dock.Bottom);
        cartDock.Children.Add(footerBar);
        var paidRow = new DockPanel { Margin = new Thickness(0, 0, 0, SmallGap) };
        var paidValue = Label("", 13, "#15803D", true);
        DockPanel.SetDock(paidValue, Dock.Right);
        paidRow.Children.Add(paidValue);
        paidRow.Children.Add(Label("Alınan ödeme", 13, Muted));
        footer.Children.Add(paidRow);
        var netRow = SummaryRow("Ara toplam (KDV hariç)", out var netValue);
        netRow.Margin = new Thickness(0);
        footer.Children.Add(netRow);
        var vatRow = SummaryRow("KDV", out var cartVatValue);
        vatRow.Margin = new Thickness(0, 4, 0, SmallGap);
        footer.Children.Add(vatRow);
        var totalRow = new DockPanel { Margin = new Thickness(0, 0, 0, Gap) };
        var totalValue = Label(Format(0), 30, bold: true);
        DockPanel.SetDock(totalValue, Dock.Right);
        totalRow.Children.Add(totalValue);
        var totalCaption = Label("Toplam", 15, Muted, true);
        totalCaption.VerticalAlignment = VerticalAlignment.Center;
        totalRow.Children.Add(totalCaption);
        footer.Children.Add(totalRow);

        var pay = new StackPanel();
        footer.Children.Add(pay);
        var payTop = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -10, 0) };
        var payBottom = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -10, -10) };
        pay.Children.Add(payTop);
        pay.Children.Add(payBottom);
        Button PayButton(UniformGrid row, string text, string glyph, string? style, Action action)
        {
            var button = Act("", action, style);
            button.Height = 60;
            button.FontSize = 14;
            button.Padding = new Thickness(4, 0, 4, 0);
            button.Margin = new Thickness(0, 0, 10, 10);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(Glyph(glyph, 16));
            content.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            button.Content = content;
            AutomationProperties.SetName(button, text);
            row.Children.Add(button);
            return button;
        }
        PayButton(payTop, "Nakit", "\uE8C7", "SuccessButton", () => CompleteSingle("Nakit"));
        PayButton(payTop, "Kredi Kartı", "\uE8C7", "PrimaryButton", () => CompleteSingle("Kart"));
        PayButton(payTop, "Havale/EFT", "\uE8AB", "PrimaryButton", () => CompleteSingle("Havale/EFT"));
        PayButton(payBottom, "Vadeli", "\uE787", "WarningButton", () => CompleteSingle("Vadeli"));
        PayButton(payBottom, "Parçalı", "\uE8C1", null, ShowSplitPayment);

        var lines = new StackPanel();
        cartDock.Children.Add(TouchScroll(lines));

        void RenderCart()
        {
            lines.Children.Clear();
            var sum = Sum(basket);
            var paid = payments.Sum(p => p.Amount);
            totalValue.Text = Format(sum);
            var breakdown = Summarize(basket);
            netValue.Text = Format(breakdown.Subtotal - breakdown.Discount);
            cartVatValue.Text = Format(breakdown.Vat);
            countText.Text = basket.Sum(l => (long)l.Quantity).ToString(culture);
            paidRow.Visibility = payments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            paidValue.Text = $"{Format(paid)} · Kalan {Format(sum - paid)}";
            var account = saleAccount.Length > 0 && accounts.TryGetValue(saleAccount, out var selected) ? selected : null;
            SetButtonContent(accountButton, account is null ? "Cari seç" : account.Name, "");
            accountButton.Style = (Style)FindResource(account is null ? typeof(Button) : "PrimaryButton");
            accountButton.MaxWidth = 170;
            if (basket.Count == 0)
            {
                var empty = new StackPanel { Margin = new Thickness(Gap, 64, Gap, Gap), HorizontalAlignment = HorizontalAlignment.Center };
                var icon = Glyph("", 40, "#D1D5DB");
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Children.Add(icon);
                var title = Label("Sepet boş", 15, "#374151", true);
                title.Margin = new Thickness(0, Gap, 0, 4);
                title.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Children.Add(title);
                var hint = Label("Bir ürüne dokun veya barkod okut.", 13, Muted);
                hint.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Children.Add(hint);
                lines.Children.Add(empty);
                return;
            }
            foreach (var line in basket) lines.Children.Add(CartRow(line));
        }
        refreshCart = RenderCart;

        UIElement CartRow(Line line)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, SmallGap, 0) };
            var name = Label(line.Name, 14, bold: true);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(name);
            var unit = Label($"{Format(line.UnitPrice)} / {(products.TryGetValue(line.Code, out var lp) ? lp.Unit.ToLower(culture) : "adet")}" + (line.Vat is int rate ? $" · KDV %{rate}" : ""), 12, Muted);
            unit.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(unit);
            row.Children.Add(text);

            var stepper = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button Step(string glyph, int delta, string? style = null)
            {
                var button = Act("", () => ChangeQuantity(line, delta), style, glyph);
                button.Width = button.Height = 36;
                button.Padding = new Thickness(0);
                return button;
            }
            stepper.Children.Add(line.Quantity == 1 ? Step("", -1, "DangerButton") : Step("", -1));
            var quantity = Label(line.Quantity.ToString(culture), 15, bold: true);
            quantity.MinWidth = 36;
            quantity.TextAlignment = TextAlignment.Center;
            quantity.VerticalAlignment = VerticalAlignment.Center;
            stepper.Children.Add(quantity);
            stepper.Children.Add(Step("", 1));
            Grid.SetColumn(stepper, 1);
            row.Children.Add(stepper);

            var amount = Label(Format(line.UnitPrice * line.Quantity), 15, bold: true);
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            amount.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(amount, 2);
            row.Children.Add(amount);
            return new Border
            {
                Child = row, Padding = new Thickness(Gap, 12, Gap, 12),
                BorderBrush = Paint("#F0F1F4"), BorderThickness = new Thickness(0, 0, 0, 1)
            };
        }

        void ChangeQuantity(Line line, int delta)
        {
            if (payments.Count > 0)
                throw new InvalidOperationException("Sepeti değiştirmek için önce ödeme parçalarını kaldır.");
            var index = basket.IndexOf(line);
            if (index < 0) return;
            if (line.Quantity + delta <= 0) basket.RemoveAt(index);
            else if (line.Quantity + delta <= 1_000_000) basket[index] = line with { Quantity = line.Quantity + delta };
            Totals();
        }

        void PickAccount()
        {
            var body = new StackPanel();
            body.Children.Add(SearchField("Cari kodu veya müşteri adı", out var query));
            var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            body.Children.Add(list);
            var modal = ShowModal("Satış carisi", body, 460, 560, "Vadeli satışlarda cari seçimi zorunludur.");
            void Render()
            {
                list.Children.Clear();
                var term = query.Text.Trim();
                var found = accounts.Values.Where(a => Matches(term, a.Code, a.Name)).OrderBy(a => a.Name).Take(60).ToArray();
                if (found.Length == 0) list.Children.Add(Label(accounts.Count == 0 ? "Henüz cari yok." : "Eşleşen cari yok.", 13, Muted));
                foreach (var account in found)
                {
                    var item = new Button
                    {
                        Style = (Style)FindResource(account.Code == saleAccount ? "SegmentActiveButton" : "GhostButton"),
                        HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 48, Margin = new Thickness(0, 0, 0, 4)
                    };
                    var row = new DockPanel();
                    var code = Label(account.Code, 12, Muted);
                    code.VerticalAlignment = VerticalAlignment.Center;
                    DockPanel.SetDock(code, Dock.Right);
                    row.Children.Add(code);
                    var name = Label(account.Name, 14, bold: true);
                    name.TextTrimming = TextTrimming.CharacterEllipsis;
                    row.Children.Add(name);
                    item.Content = row;
                    item.Click += (_, _) => { saleAccount = account.Code; modal.Close(); Totals(); };
                    list.Children.Add(item);
                }
            }
            query.TextChanged += (_, _) => Render();
            Render();
            if (saleAccount.Length > 0)
                Append(modal.Footer, Act("Cariyi kaldır", () => { saleAccount = ""; modal.Close(); Totals(); }, "DangerButton", ""));
            Append(modal.Footer, Act("Kapat", modal.Close));
        }

        void CompleteSingle(string method)
        {
            if (payments.Count > 0)
                throw new InvalidOperationException("Ödeme parçaları var. Parçalı ödemeyi tamamla veya parçaları kaldır.");
            if (basket.Count == 0) throw new InvalidOperationException("Sepete ürün ekle.");
            var value = Sum(basket);
            if (value <= 0 && method == "Vadeli")
                throw new InvalidOperationException("Vadeli satış için pozitif bir sepet toplamı gerekiyor.");
            if (method == "Vadeli" && saleAccount.Length == 0)
            {
                PickAccount();
                throw new InvalidOperationException("Vadeli satış için önce cari seç.");
            }
            CompleteSale(value == 0 ? [] : [new Payment(method, value, "")]);
        }

        void ShowSplitPayment()
        {
            if (basket.Count == 0) throw new InvalidOperationException("Sepete ürün ekle.");
            var body = new StackPanel();
            var summary = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -SmallGap, Gap) };
            body.Children.Add(summary);
            TextBlock Stat(string caption)
            {
                var box = new StackPanel();
                box.Children.Add(Label(caption, 12, Muted, true));
                var value = Label("", 17, bold: true);
                value.Margin = new Thickness(0, 4, 0, 0);
                box.Children.Add(value);
                summary.Children.Add(new Border
                {
                    Child = box, Background = Paint("#F9FAFB"), CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12), Margin = new Thickness(0, 0, SmallGap, 0)
                });
                return value;
            }
            var totalStat = Stat("Toplam");
            var paidStat = Stat("Ödenen");
            var leftStat = Stat("Kalan");

            var amountRow = new Grid();
            amountRow.ColumnDefinitions.Add(new ColumnDefinition());
            amountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var amount = new TextBox { MinHeight = 48, FontSize = 18 };
            amountRow.Children.Add(amount);
            var fill = Act("Kalanı yaz", () => amount.Text = ((Sum(basket) - payments.Sum(p => p.Amount)) / 100m).ToString("F2", culture));
            fill.MinHeight = 48;
            fill.Margin = new Thickness(SmallGap, 0, 0, 0);
            Grid.SetColumn(fill, 1);
            amountRow.Children.Add(fill);
            body.Children.Add(Field("Ödeme tutarı (TL)", amountRow));

            var methods = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, -SmallGap, 0) };
            body.Children.Add(methods);
            foreach (var (method, style) in new[] { ("Nakit", "SuccessButton"), ("Kart", "PrimaryButton"), ("Havale/EFT", "PrimaryButton") })
            {
                var add = Act($"{MethodName(method)} ekle", () => Pay(method), style, "");
                add.MinHeight = 48;
                add.Margin = new Thickness(0, 0, SmallGap, 0);
                methods.Children.Add(add);
            }
            var partsTitle = Label("Ödeme parçaları", 13, "#374151", true);
            partsTitle.Margin = new Thickness(0, PagePadding, 0, SmallGap);
            body.Children.Add(partsTitle);
            var parts = new StackPanel();
            body.Children.Add(parts);

            var modal = ShowModal("Parçalı ödeme", body, 480);
            Append(modal.Footer, Act("Kapat", modal.Close));
            var complete = Append(modal.Footer, Act("Satışı tamamla", () => CompleteSale(payments.ToArray()), "SuccessButton", ""));
            amount.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Run(() => Pay("Nakit")); } };

            void Pay(string method)
            {
                var value = Money(amount);
                if (value > Sum(basket) - payments.Sum(p => p.Amount))
                    throw new InvalidOperationException("Ödeme, kalan tutarı aşıyor.");
                payments.Add(new(method, value, ""));
                amount.Clear();
                amount.Focus();
            }
            void Render()
            {
                var sum = Sum(basket);
                var paid = payments.Sum(p => p.Amount);
                totalStat.Text = Format(sum);
                paidStat.Text = Format(paid);
                leftStat.Text = Format(sum - paid);
                leftStat.Foreground = Paint(sum - paid == 0 ? "#15803D" : "#B45309");
                complete.IsEnabled = sum - paid == 0 && payments.Count > 0;
                parts.Children.Clear();
                if (payments.Count == 0) parts.Children.Add(Label("Henüz ödeme eklenmedi.", 13, Muted));
                foreach (var payment in payments.ToArray())
                {
                    var row = new DockPanel { Margin = new Thickness(0, 0, 0, SmallGap) };
                    var remove = Act("", () => payments.Remove(payment), "GhostButton", "");
                    remove.Width = 34;
                    DockPanel.SetDock(remove, Dock.Right);
                    row.Children.Add(remove);
                    var value = Label(Format(payment.Amount), 14, bold: true);
                    value.VerticalAlignment = VerticalAlignment.Center;
                    value.Margin = new Thickness(0, 0, SmallGap, 0);
                    DockPanel.SetDock(value, Dock.Right);
                    row.Children.Add(value);
                    var method = Label(MethodName(payment.Method), 14);
                    method.VerticalAlignment = VerticalAlignment.Center;
                    row.Children.Add(method);
                    parts.Children.Add(new Border
                    {
                        Child = row, BorderBrush = Paint(LineColor), BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 4, 4, 4)
                    });
                }
                Totals();
            }
            System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) => Render();
            payments.CollectionChanged += changed;
            modal.Closed = () => payments.CollectionChanged -= changed;
            splitClose = modal.Close;
            Render();
        }

        void CompleteSale(Payment[] tender)
        {
            if (basket.Count == 0 || tender.Sum(p => p.Amount) != Sum(basket))
                throw new InvalidOperationException("Sepet ve ödeme toplamını kontrol et.");
            foreach (var group in basket.GroupBy(line => line.Code))
                if (!products.ContainsKey(group.Key) || stock.GetValueOrDefault(group.Key).Shop < group.Sum(line => line.Quantity))
                    throw new InvalidOperationException($"Dükkân stoku yetersiz: {group.Key}");
            var id = saleAccount;
            if (tender.Any(p => p.Method == "Vadeli") && id.Length == 0)
                throw new InvalidOperationException("Vadeli satış için cari seç.");
            if (id.Length > 0 && !accounts.ContainsKey(id))
                throw new InvalidOperationException("Seçilen cari bulunamadı.");
            var total = Sum(basket);
            var wasLow = products.Values.Where(IsLow).Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Save("sale", new Sale(id, basket.ToArray(), tender));
            var nowLow = products.Values.Where(p => IsLow(p) && !wasLow.Contains(p.Code)).ToArray();
            if (nowLow.Length > 0)
                Toast(nowLow.Length == 1 ? $"Stok azaldı: {nowLow[0].Name} raf alt sınırına indi ({stock.GetValueOrDefault(nowLow[0].Code).Shop} {nowLow[0].Unit.ToLower(culture)})."
                    : $"Stok azaldı: {nowLow.Length} ürün raf alt sınırına indi.", true);
            basket.Clear();
            payments.Clear();
            saleAccount = "";
            splitClose?.Invoke();
            splitClose = null;
            Totals();
            Toast($"Satış tamamlandı · {Format(total)}");
            saleSearch?.Focus();
        }

        RenderCart();
    }

    private Action? splitClose;

    private static void Print(FlowDocument document, string title)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        document.PageWidth = dialog.PrintableAreaWidth;
        document.PageHeight = dialog.PrintableAreaHeight;
        document.ColumnWidth = document.PageWidth;
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, title);
    }

    private void Columns(DataGrid grid, params (string Heading, string Path, double Weight)[] columns)
    {
        grid.AutoGenerateColumns = false;
        foreach (var column in columns)
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Heading, Binding = new Binding(column.Path)
                {
                    Converter = column.Path is "UnitPrice" or "Amount" or "Price" or "Total" ? new MoneyConverter(culture) : null
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
