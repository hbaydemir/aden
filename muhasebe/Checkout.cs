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

        var top = new Grid { Margin = new Thickness(0, 0, 0, Gap) };
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        DockPanel.SetDock(top, Dock.Top);
        catalog.Children.Add(top);
        var searchHost = SearchField("Barkod okut, ürün adı veya kodu yaz", out var search, large: true);
        saleSearch = search;
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

        var history = Act("Satış geçmişi", ShowSalesHistory, glyph: "");
        history.MinHeight = 52;
        history.Padding = new Thickness(Gap, 0, Gap, 0);
        history.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(history, 2);
        top.Children.Add(history);

        var expand = new Button { MinHeight = 52, Padding = new Thickness(Gap, 0, Gap, 0), Margin = new Thickness(12, 0, 0, 0) };
        expand.Click += (_, _) => SetFullscreen(!fullscreen);
        fullscreenChanged = () => SetButtonContent(expand, fullscreen ? "Küçült" : "Tam ekran", fullscreen ? "" : "");
        fullscreenChanged();
        expand.ToolTip = "Tam ekran (F11)";
        Grid.SetColumn(expand, 3);
        top.Children.Add(expand);

        var info = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(info, Dock.Top);
        catalog.Children.Add(info);
        var pager = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(pager, Dock.Right);
        info.Children.Add(pager);
        var result = Label("", 13, Muted);
        result.VerticalAlignment = VerticalAlignment.Center;
        info.Children.Add(result);

        var tiles = new WrapPanel();
        var scroll = TouchScroll(tiles);
        scroll.Margin = new Thickness(0, 0, -12, 0);
        catalog.Children.Add(scroll);

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

        var page = 0;
        const int pageSize = 60;
        const double tileGap = 12;
        void RenderCatalog()
        {
            tiles.Children.Clear();
            pager.Children.Clear();
            var term = search.Text.Trim();
            var matched = products.Values
                .Where(product => term.Length == 0 || product.Barcode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    product.Code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    culture.CompareInfo.IndexOf(product.Name, term, CompareOptions.IgnoreCase) >= 0)
                .OrderBy(product => product.Name, StringComparer.Create(culture, true)).ToArray();
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
                var go = Act("Ürünlere git", () => SelectPage(productsPage), "PrimaryButton", "");
                go.HorizontalAlignment = HorizontalAlignment.Left;
                empty.Children.Add(go);
                tiles.Children.Add(empty);
            }
            var size = TileSize();
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

        Button Tile(Product product, double size)
        {
            var available = stock.GetValueOrDefault(product.Code).Shop;
            var tile = new Button
            {
                Style = (Style)FindResource("PosTile"), Width = size, Height = size,
                Margin = new Thickness(0, 0, tileGap, tileGap)
            };
            tile.Click += (_, _) => Run(() => AddProduct(product));
            var card = new DockPanel();
            var price = Label(Format(product.Price), 18, "#2563EB", true);
            DockPanel.SetDock(price, Dock.Bottom);
            card.Children.Add(price);
            var badge = new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Paint(available > 0 ? "#F0FDF4" : "#FEF2F2"),
                Child = Label(available > 0 ? $"{available} adet" : "Stok yok", 11, available > 0 ? "#15803D" : "#DC2626", true)
            };
            DockPanel.SetDock(badge, Dock.Bottom);
            card.Children.Add(badge);
            var code = Label(product.Code, 11, Muted);
            code.TextTrimming = TextTrimming.CharacterEllipsis;
            DockPanel.SetDock(code, Dock.Top);
            card.Children.Add(code);
            var name = Label(product.Name, 15, bold: true);
            name.Margin = new Thickness(0, 4, 0, 0);
            name.TextWrapping = TextWrapping.Wrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            card.Children.Add(name);
            tile.Content = card;
            AutomationProperties.SetName(tile, $"{product.Name}, {Format(product.Price)}, dükkânda {available} adet");
            return tile;
        }

        double TileSize()
        {
            var width = scroll.ViewportWidth;
            if (width <= 0) return 160;
            var columns = Math.Max(2, (int)(width / (140 + tileGap)));
            return Math.Floor(width / columns) - tileGap;
        }

        scroll.ScrollChanged += (_, e) =>
        {
            if (e.ViewportWidthChange == 0) return;
            var size = TileSize();
            foreach (var tile in tiles.Children.OfType<Button>()) tile.Width = tile.Height = size;
        };
        refreshCatalog = RenderCatalog;
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
        var totalRow = new DockPanel { Margin = new Thickness(0, 0, 0, Gap) };
        var totalValue = Label(Format(0), 30, bold: true);
        DockPanel.SetDock(totalValue, Dock.Right);
        totalRow.Children.Add(totalValue);
        var totalCaption = Label("Toplam", 15, Muted, true);
        totalCaption.VerticalAlignment = VerticalAlignment.Center;
        totalRow.Children.Add(totalCaption);
        footer.Children.Add(totalRow);

        var pay = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -10, -10) };
        footer.Children.Add(pay);
        Button PayButton(string text, string glyph, string? style, Action action)
        {
            var button = Act("", action, style);
            button.Height = 64;
            button.FontSize = 16;
            button.Margin = new Thickness(0, 0, 10, 10);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(Glyph(glyph, 18));
            content.Children.Add(new TextBlock { Text = text, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            button.Content = content;
            AutomationProperties.SetName(button, text);
            pay.Children.Add(button);
            return button;
        }
        PayButton("Nakit", "", "SuccessButton", () => CompleteSingle("Nakit"));
        PayButton("Kart", "", "PrimaryButton", () => CompleteSingle("Kart"));
        PayButton("Vadeli", "", "WarningButton", () => CompleteSingle("Vadeli"));
        PayButton("Parçalı", "", null, ShowSplitPayment);

        var lines = new StackPanel();
        cartDock.Children.Add(TouchScroll(lines));

        void RenderCart()
        {
            lines.Children.Clear();
            var sum = Sum(basket);
            var paid = payments.Sum(p => p.Amount);
            totalValue.Text = Format(sum);
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
            var unit = Label($"{Format(line.UnitPrice)} / adet", 12, Muted);
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

            var methods = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, -SmallGap, 0) };
            body.Children.Add(methods);
            foreach (var (method, style) in new[] { ("Nakit", "SuccessButton"), ("Kart", "PrimaryButton") })
            {
                var add = Act($"{method} ekle", () => Pay(method), style, "");
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
                    var method = Label(payment.Method, 14);
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
            Save("sale", new Sale(id, basket.ToArray(), tender));
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

    private record SaleRow(Guid Satış, DateTime Tarih, string Cari, string Ürünler, string Toplam, string Ödemeler);

    private void ShowSalesHistory()
    {
        var body = new DockPanel();
        var tools = new DockPanel { Margin = new Thickness(0, 0, 0, Gap) };
        DockPanel.SetDock(tools, Dock.Top);
        body.Children.Add(tools);
        var summary = Label("", 13, Muted);
        summary.VerticalAlignment = VerticalAlignment.Center;
        summary.Margin = new Thickness(Gap, 0, 0, 0);
        DockPanel.SetDock(summary, Dock.Right);
        tools.Children.Add(summary);
        tools.Children.Add(SearchField("Cari, ürün veya ödeme ara", out var query));
        var grid = Table();
        grid.AutoGenerateColumns = false;
        grid.Height = 420;
        void Column(string header, string path, double weight, string? format = null) => grid.Columns.Add(new DataGridTextColumn
        {
            Header = header, Binding = new Binding(path) { StringFormat = format },
            Width = new DataGridLength(weight, DataGridLengthUnitType.Star)
        });
        Column("Tarih", nameof(SaleRow.Tarih), 1, "dd.MM.yyyy HH:mm");
        Column("Cari", nameof(SaleRow.Cari), 0.8);
        Column("Ürünler", nameof(SaleRow.Ürünler), 2.4);
        Column("Toplam", nameof(SaleRow.Toplam), 0.8);
        Column("Ödemeler", nameof(SaleRow.Ödemeler), 1.4);
        body.Children.Add(Card(grid));
        var all = Read<Sale>("sale").Reverse().Select(s => new SaleRow(s.Entry.Id, s.Entry.CreatedAt.LocalDateTime, s.Value.Account,
            string.Join(", ", s.Value.Lines.Select(l => $"{l.Name} × {l.Quantity}")),
            Format(Sum(s.Value.Lines)),
            string.Join(", ", s.Value.Payments.Select(p => $"{p.Method} {Format(p.Amount)} {p.Reference}".Trim())))).ToArray();
        var todayTotal = Read<Sale>("sale").Where(s => s.Entry.CreatedAt.LocalDateTime.Date == DateTime.Today).Sum(s => Sum(s.Value.Lines));
        summary.Text = $"Bugün {Format(todayTotal)}";
        void Filter()
        {
            var term = query.Text.Trim();
            grid.ItemsSource = all.Where(r => Matches(term, r.Cari, r.Ürünler, r.Ödemeler)).ToArray();
        }
        query.TextChanged += (_, _) => Filter();
        Filter();
        var modal = ShowModal("Satış geçmişi", body, 1040, subtitle: $"{all.Length} tamamlanan satış");
        Append(modal.Footer, Act("Kapat", modal.Close));
        Append(modal.Footer, Act("Satış özetini yazdır", () =>
        {
            if (grid.SelectedItem is not SaleRow selected) throw new InvalidOperationException("Listeden bir satış seç.");
            PrintSale(selected.Satış);
        }, "PrimaryButton", ""));
    }

    private void PrintSale(Guid saleId)
    {
        var sale = Read<Sale>("sale").Single(s => s.Entry.Id == saleId);
        var document = new FlowDocument { PagePadding = new Thickness(45), FontFamily = FontFamily };
        document.Blocks.Add(new Paragraph(new Bold(new Run(store.Setting("company", "Aden")))) { FontSize = 24 });
        document.Blocks.Add(new Paragraph(new Run($"SATIŞ ÖZETİ · Mali belge yerine geçmez\n{sale.Entry.CreatedAt.LocalDateTime:g}\n{saleId}")));
        foreach (var line in sale.Value.Lines)
            document.Blocks.Add(new Paragraph(new Run($"{line.Name} × {line.Quantity}   {Format(line.UnitPrice * line.Quantity)}")));
        document.Blocks.Add(new Paragraph(new Bold(new Run($"Toplam {Format(Sum(sale.Value.Lines))}"))));
        foreach (var payment in sale.Value.Payments)
            document.Blocks.Add(new Paragraph(new Run($"{payment.Method}   {Format(payment.Amount)}   {payment.Reference}")));
        Print(document, "Aden satış özeti");
    }

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
