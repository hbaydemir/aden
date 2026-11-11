using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;

namespace Aden;

/// <summary>Satış geçmişi: düzenleme, silme, iptal, iade ve değişim.</summary>
public sealed partial class MainWindow
{
    private static string MethodName(string method) => method == "Kart" ? "Kredi Kartı" : method;

    private static readonly string[] TenderMethods = ["Nakit", "Kart", "Havale/EFT", "Vadeli"];

    private sealed record SaleRow(Guid Satış, DateTime Tarih, string Cari, string Ürünler, string Toplam, string Ödemeler, string Durum);

    private static string LineKey(Line line) => $"{line.Code}|{line.Name}|{line.UnitPrice}";

    /// <summary>Satıştaki satırların iade/değişim sonrası kalan adetleri (aynı ürün, ad ve fiyat birleşir).</summary>
    private List<Line> RemainingLines(SaleRecord sale)
    {
        var lines = new Dictionary<string, Line>();
        void Add(Line line, int sign)
        {
            var key = LineKey(line);
            lines[key] = lines.TryGetValue(key, out var old) ? old with { Quantity = old.Quantity + sign * line.Quantity }
                : line with { Quantity = sign * line.Quantity };
        }
        foreach (var line in sale.Value.Lines) Add(line, 1);
        foreach (var item in returns.Where(r => r.Value.SaleId == sale.Id))
        {
            foreach (var line in item.Value.Returned) Add(line, -1);
            foreach (var line in item.Value.Given) Add(line, 1);
        }
        return lines.Values.Where(l => l.Quantity > 0).ToList();
    }

    private string SaleStatus(SaleRecord sale)
    {
        var related = returns.Where(r => r.Value.SaleId == sale.Id).ToArray();
        var state = related.Length == 0 ? "" : RemainingLines(sale).Count == 0 ? "İptal / tamamen iade"
            : string.Join(", ", related.Select(r => r.Value.Type).Distinct());
        return string.Join(" · ", new[] { sale.Edited ? "Düzenlendi" : "", state }.Where(s => s.Length > 0));
    }

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
        tools.Children.Add(SearchField("Cari, ürün, ödeme veya durum ara", out var query));
        var grid = Table();
        grid.AutoGenerateColumns = false;
        grid.Height = 440;
        void Column(string header, string path, double weight, string? format = null) => grid.Columns.Add(new DataGridTextColumn
        {
            Header = header, Binding = new Binding(path) { StringFormat = format },
            Width = new DataGridLength(weight, DataGridLengthUnitType.Star)
        });
        Column("Tarih", nameof(SaleRow.Tarih), 1, "dd.MM.yyyy HH:mm");
        Column("Cari", nameof(SaleRow.Cari), 0.9);
        Column("Ürünler", nameof(SaleRow.Ürünler), 2.2);
        Column("Toplam", nameof(SaleRow.Toplam), 0.8);
        Column("Ödemeler", nameof(SaleRow.Ödemeler), 1.3);
        Column("Durum", nameof(SaleRow.Durum), 1);
        body.Children.Add(Card(grid));
        SaleRow[] all = [];
        void Load()
        {
            all = sales.AsEnumerable().Reverse().Select(s => new SaleRow(s.Id, s.Date, s.Value.Account.Length > 0 ? AccountName(s.Value.Account) : "",
                string.Join(", ", s.Value.Lines.Select(l => $"{l.Name} × {l.Quantity}")),
                Format(Sum(s.Value.Lines)),
                string.Join(", ", s.Value.Payments.Select(p => $"{MethodName(p.Method)} {Format(p.Amount)} {p.Reference}".Trim())),
                SaleStatus(s))).ToArray();
            var todayTotal = sales.Where(s => s.Date.Date == DateTime.Today).Sum(s => Sum(s.Value.Lines)) +
                returns.Where(r => r.Date.Date == DateTime.Today).Sum(r => Sum(r.Value.Given) - Sum(r.Value.Returned));
            summary.Text = $"{all.Length} satış · Bugün {Format(todayTotal)}";
            Filter();
        }
        void Filter()
        {
            var term = query.Text.Trim();
            grid.ItemsSource = all.Where(r => Matches(term, r.Cari, r.Ürünler, r.Ödemeler, r.Durum)).ToArray();
        }
        query.TextChanged += (_, _) => Filter();
        Load();
        var modal = ShowModal("Satış geçmişi", body, 1120, subtitle: "Satışı seç: düzenle, sil, iptal et, iade al veya değişim yap.");
        refreshers.Add(Load);
        modal.Closed = () => refreshers.Remove(Load);

        SaleRecord Selected() => grid.SelectedItem is SaleRow row && sales.FirstOrDefault(s => s.Id == row.Satış) is { } sale
            ? sale : throw new InvalidOperationException("Listeden bir satış seç.");
        OnOpen(grid, () => EditSale(Selected()));
        Append(modal.Footer, Act("Kapat", modal.Close));
        Append(modal.Footer, Act("Excel / CSV", () => ShowDataTransfer("Satışlar"), glyph: ""));
        Append(modal.Footer, Act("Yazdır", () => PrintSale(Selected().Id), glyph: ""));
        Append(modal.Footer, Act("Sil", () => DeleteSale(Selected()), "DangerButton", ""));
        Append(modal.Footer, Act("Düzenle", () => EditSale(Selected()), glyph: ""));
        Append(modal.Footer, Act("İptal / İade / Değişim", () => ReturnSale(Selected()), "WarningButton", ""));
    }

    private void DeleteSale(SaleRecord sale)
    {
        var related = returns.Count(r => r.Value.SaleId == sale.Id);
        Confirm("Satışı sil", $"{sale.Date:dd.MM.yyyy HH:mm} tarihli {Format(Sum(sale.Value.Lines))} tutarındaki satış kayıtlardan silinecek. " +
            "Ürünler dükkân stokuna geri döner ve satış raporlardan çıkar." +
            (related > 0 ? $" Bu satışa bağlı {related} iade/değişim kaydı da geçersiz olur." : "") +
            " Müşteriye iade yapıldıysa silmek yerine \"İptal / İade\" kullan.", "Sil", () =>
        {
            Save("sale-revision", new SaleRevision(sale.Id, null, "Silindi"));
            Toast("Satış silindi.");
        });
    }

    /// <summary>Satırları, cariyi ve ödemeyi düzenler.</summary>
    private void EditSale(SaleRecord sale)
    {
        if (returns.Any(r => r.Value.SaleId == sale.Id))
            throw new InvalidOperationException("Bu satışta iade/değişim kaydı var. Değişiklik için \"İptal / İade / Değişim\" kullan.");
        var lines = sale.Value.Lines.ToList();
        Action addLater = () => { };
        var originalTotal = Sum(sale.Value.Lines);
        var root = new StackPanel();
        root.Children.Add(Section("Kalemler"));
        var productBox = ProductBox();
        var count = Text("1");
        var addRow = new Grid();
        addRow.ColumnDefinitions.Add(new ColumnDefinition());
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addRow.Children.Add(Field("Ürün ekle (ad, kod veya barkod)", productBox));
        var countField = Field("Adet", count);
        countField.Margin = new Thickness(SmallGap, 0, 0, 0);
        Grid.SetColumn(countField, 1);
        addRow.Children.Add(countField);
        var add = Act("Ekle", () => addLater(), "SuccessButton", "");
        add.VerticalAlignment = VerticalAlignment.Bottom;
        add.Margin = new Thickness(SmallGap, 0, 0, 0);
        Grid.SetColumn(add, 2);
        addRow.Children.Add(add);
        root.Children.Add(addRow);
        productBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Run(addLater); } };
        var list = new StackPanel();
        var listCard = Card(list);
        listCard.Margin = new Thickness(0, Gap, 0, 0);
        root.Children.Add(listCard);
        var totalRow = SummaryRow("Yeni toplam", out var totalValue, 16, true, "#1D4ED8");
        root.Children.Add(totalRow);

        root.Children.Add(Section("Cari ve ödeme", PagePadding));
        var account = AccountBox(sale.Value.Account.Length > 0 ? sale.Value.Account : null);
        var method = new ComboBox();
        var reason = Text();
        reason.MaxLength = 300;
        root.Children.Add(Form(2, F(Field("Cari (isteğe bağlı)", account)), F(Field("Ödeme", method, true)),
            F(Field("Düzenleme nedeni", Hinted(reason, "Örn. yanlış ürün okutuldu")), 2)));
        var keep = "Mevcut ödemeler: " + string.Join(", ", sale.Value.Payments.Select(p => $"{MethodName(p.Method)} {Format(p.Amount)}"));

        void Render()
        {
            list.Children.Clear();
            if (lines.Count == 0) list.Children.Add(Padded(Label("Kalem yok. Satışı tamamen kaldırmak için \"Sil\" kullan.", 13, Muted)));
            for (var i = 0; i < lines.Count; i++) list.Children.Add(Row(i));
            totalValue.Text = Format(Sum(lines));
            var selected = method.SelectedItem as string;
            var items = new List<string>();
            if (Sum(lines) == originalTotal && sale.Value.Payments.Length > 0) items.Add(keep);
            items.AddRange(TenderMethods.Select(MethodName));
            method.ItemsSource = items;
            method.SelectedItem = selected is not null && items.Contains(selected) ? selected : items[0] == keep ? keep
                : MethodName(sale.Value.Payments.FirstOrDefault()?.Method ?? "Nakit");
        }
        static Border Padded(UIElement child) => new() { Child = child, Padding = new Thickness(Gap, 14, Gap, 14) };
        UIElement Row(int index)
        {
            var line = lines[index];
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = Label(line.Name, 14, bold: true);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            names.Children.Add(title);
            names.Children.Add(Label($"{line.Code} · {Format(line.UnitPrice)} / birim", 12, Muted));
            grid.Children.Add(names);
            var stepper = Stepper(line.Quantity, value => { lines[index] = line with { Quantity = value }; Render(); });
            Grid.SetColumn(stepper, 1);
            grid.Children.Add(stepper);
            var amount = Label(Format(line.UnitPrice * line.Quantity), 14, bold: true);
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            amount.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(amount, 2);
            grid.Children.Add(amount);
            var remove = Act("", () => { lines.RemoveAt(index); Render(); }, "GhostButton", "");
            remove.Width = 36;
            remove.Foreground = Paint("#DC2626");
            remove.Margin = new Thickness(SmallGap, 0, 0, 0);
            AutomationProperties.SetName(remove, $"{line.Name} kaldır");
            Grid.SetColumn(remove, 3);
            grid.Children.Add(remove);
            return new Border
            {
                Child = grid, Padding = new Thickness(Gap, 10, 10, 10), BorderBrush = Paint("#F0F1F4"),
                BorderThickness = new Thickness(0, 0, 0, index == lines.Count - 1 ? 0 : 1)
            };
        }
        void AddLine()
        {
            var product = ResolveProduct(productBox);
            var quantity = Quantity(count);
            void Put(string? variant)
            {
                var line = LineFor(product, quantity);
                if (variant is not null) line = line with { Name = $"{product.Name} · {variant}" };
                var existing = lines.FindIndex(l => LineKey(l) == LineKey(line));
                if (existing >= 0) lines[existing] = lines[existing] with { Quantity = lines[existing].Quantity + quantity };
                else lines.Add(line);
                productBox.Text = "";
                count.Text = "1";
                Render();
            }
            if (OptionsOf(product).Length > 0) PickVariant(product, Put);
            else Put(null);
        }
        addLater = AddLine;
        Render();

        var modal = ShowModal("Satışı düzenle", root, 760, 820, $"{sale.Date:dd.MM.yyyy HH:mm} · ilk toplam {Format(originalTotal)}");
        Append(modal.Footer, Act("Vazgeç", modal.Close));
        Append(modal.Footer, Act("Kaydet", () =>
        {
            if (lines.Count == 0) throw new InvalidOperationException("En az bir kalem olmalı. Satışı kaldırmak için \"Sil\" kullan.");
            // Stok: düzenlenen satış eski adetleri geri verip yenilerini düşer.
            foreach (var group in lines.GroupBy(l => l.Code, StringComparer.OrdinalIgnoreCase))
            {
                var before = sale.Value.Lines.Where(l => l.Code.Equals(group.Key, StringComparison.OrdinalIgnoreCase)).Sum(l => l.Quantity);
                var need = group.Sum(l => l.Quantity) - before;
                if (need > 0 && stock.GetValueOrDefault(group.Key).Shop < need)
                    throw new InvalidOperationException($"Dükkân stoku yetersiz: {ProductName(group.Key)}");
            }
            var target = TryAccount(account);
            if (account.Text.Trim().Length > 0 && target is null) throw new InvalidOperationException("Geçerli bir cari seç veya alanı boş bırak.");
            var choice = method.SelectedItem as string ?? throw new InvalidOperationException("Ödeme yöntemini seç.");
            var total = Sum(lines);
            Payment[] tender = choice == keep ? sale.Value.Payments
                : total == 0 ? [] : [new Payment(TenderMethods.First(m => MethodName(m) == choice), total, "")];
            if (tender.Any(p => p.Method == "Vadeli") && target is null) throw new InvalidOperationException("Vadeli ödeme için cari seç.");
            Save("sale-revision", new SaleRevision(sale.Id, new Sale(target?.Code ?? "", lines.ToArray(), tender), reason.Text.Trim()));
            modal.Close();
            Toast("Satış güncellendi.");
        }, "PrimaryButton", ""));
    }

    /// <summary>− adet + kutusu.</summary>
    private StackPanel Stepper(int value, Action<int> changed, int min = 1, int? max = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Button Step(string glyph, int delta)
        {
            var button = Act("", () =>
            {
                var next = value + delta;
                if (next < min || next > (max ?? 1_000_000)) return;
                changed(next);
            }, glyph: glyph);
            button.Width = button.Height = button.MinHeight = 32;
            button.Padding = new Thickness(0);
            AutomationProperties.SetName(button, delta < 0 ? "Azalt" : "Artır");
            return button;
        }
        row.Children.Add(Step("", -1));
        var text = Label(value.ToString(culture), 14, bold: true);
        text.MinWidth = 40;
        text.TextAlignment = TextAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        row.Children.Add(Step("", 1));
        return row;
    }

    /// <summary>İptal (tüm satış), iade (seçili ürünler) veya değişim (iade + yerine verilen ürünler).</summary>
    private void ReturnSale(SaleRecord sale)
    {
        var remaining = RemainingLines(sale);
        if (remaining.Count == 0) throw new InvalidOperationException("Bu satıştaki tüm ürünler zaten iade edilmiş.");
        var type = "İade";
        var picked = remaining.ToDictionary(LineKey, _ => 0);
        var given = new List<Line>();
        Action render = () => { };
        Action addGiven = () => { };
        var root = new StackPanel();
        var typeBar = new StackPanel { Orientation = Orientation.Horizontal };
        root.Children.Add(Field("İşlem", new Border
        {
            Child = typeBar, Background = Paint("#F3F4F6"), CornerRadius = new System.Windows.CornerRadius(10),
            Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left
        }));
        var typeButtons = new Dictionary<string, Button>();
        foreach (var (key, title) in new[] { ("İptal", "Siparişi iptal et"), ("İade", "İade al"), ("Değişim", "Değişim yap") })
        {
            var button = new Button { Content = title, Padding = new Thickness(16, 0, 16, 0) };
            if (typeBar.Children.Count > 0) button.Margin = new Thickness(4, 0, 0, 0);
            button.Click += (_, _) => { type = key; if (key == "İptal") foreach (var line in remaining) picked[LineKey(line)] = line.Quantity; render(); };
            typeBar.Children.Add(button);
            typeButtons[key] = button;
        }
        var typeHint = Label("", 12, Muted);
        typeHint.TextWrapping = TextWrapping.Wrap;
        typeHint.Margin = new Thickness(0, SmallGap, 0, 0);
        root.Children.Add(typeHint);

        root.Children.Add(Section("İade edilecek ürünler", PagePadding));
        var selectAll = Act("Tümünü seç", () => { foreach (var line in remaining) picked[LineKey(line)] = line.Quantity; render(); }, glyph: "");
        var clearAll = Act("Seçimi temizle", () => { foreach (var line in remaining) picked[LineKey(line)] = 0; render(); }, glyph: "");
        var selectRow = Hstack(selectAll, clearAll);
        selectRow.Margin = new Thickness(0, 0, 0, SmallGap);
        root.Children.Add(selectRow);
        var returnList = new StackPanel();
        root.Children.Add(Card(returnList));

        var givenPanel = new StackPanel();
        givenPanel.Children.Add(Section("Yerine verilen ürünler", PagePadding));
        var productBox = ProductBox();
        var count = Text("1");
        var addRow = new Grid();
        addRow.ColumnDefinitions.Add(new ColumnDefinition());
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addRow.Children.Add(Field("Ürün (ad, kod veya barkod)", productBox));
        var countField = Field("Adet", count);
        countField.Margin = new Thickness(SmallGap, 0, 0, 0);
        Grid.SetColumn(countField, 1);
        addRow.Children.Add(countField);
        var add = Act("Ekle", () => addGiven(), "SuccessButton", "");
        add.VerticalAlignment = VerticalAlignment.Bottom;
        add.Margin = new Thickness(SmallGap, 0, 0, 0);
        Grid.SetColumn(add, 2);
        addRow.Children.Add(add);
        givenPanel.Children.Add(addRow);
        productBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Run(addGiven); } };
        var givenList = new StackPanel();
        var givenCard = Card(givenList);
        givenCard.Margin = new Thickness(0, Gap, 0, 0);
        givenPanel.Children.Add(givenCard);
        root.Children.Add(givenPanel);

        root.Children.Add(Section("Fark ve ödeme", PagePadding));
        var totals = new StackPanel();
        totals.Children.Add(SummaryRow("İade edilen tutar", out var returnedValue));
        totals.Children.Add(SummaryRow("Verilen ürünler", out var givenValue));
        totals.Children.Add(SummaryRow("Fark", out var differenceValue, 17, true));
        var totalsCard = Card(totals, new Thickness(Gap, 10, Gap, 12));
        totalsCard.Background = Paint("#F9FAFB");
        root.Children.Add(totalsCard);
        var method = new ComboBox();
        var reason = Text();
        reason.MaxLength = 300;
        var methodField = Field("Ödeme yöntemi", method, true);
        root.Children.Add(Form(2, F(methodField), F(Field("Neden", Hinted(reason, "Örn. hatalı ürün, beden uymadı")))));
        root.Children[^1].SetValue(MarginProperty, new Thickness(0, Gap, 0, 0));

        List<Line> Returned() => remaining.Where(l => picked[LineKey(l)] > 0).Select(l => l with { Quantity = picked[LineKey(l)] }).ToList();
        long Difference() => Sum(given) - Sum(Returned());

        void Render()
        {
            foreach (var (key, button) in typeButtons)
                button.Style = (Style)FindResource(key == type ? "SegmentActiveButton" : "SegmentButton");
            typeHint.Text = type switch
            {
                "İptal" => "Satıştaki kalan tüm ürünler iade alınır, ürünler dükkân stokuna döner ve ödeme müşteriye geri verilir.",
                "Değişim" => "Seçili ürünler iade alınır, yerine verilen ürünler stoktan düşer. Fark müşteriden alınır veya müşteriye ödenir.",
                _ => "Seçili ürünler ve adetler iade alınır, dükkân stokuna geri döner. Tutar müşteriye geri ödenir."
            };
            selectRow.Visibility = type == "İptal" ? Visibility.Collapsed : Visibility.Visible;
            givenPanel.Visibility = type == "Değişim" ? Visibility.Visible : Visibility.Collapsed;
            if (type != "Değişim") given.Clear();

            returnList.Children.Clear();
            for (var i = 0; i < remaining.Count; i++)
            {
                var line = remaining[i];
                var key = LineKey(line);
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                var check = new CheckBox { IsChecked = picked[key] > 0, IsEnabled = type != "İptal", VerticalAlignment = VerticalAlignment.Center, MinHeight = 0 };
                AutomationProperties.SetName(check, $"{line.Name} iade");
                check.Checked += (_, _) => { if (picked[key] == 0) { picked[key] = line.Quantity; Render(); } };
                check.Unchecked += (_, _) => { picked[key] = 0; Render(); };
                grid.Children.Add(check);
                var names = new StackPanel { Margin = new Thickness(SmallGap, 0, SmallGap, 0), VerticalAlignment = VerticalAlignment.Center };
                var title = Label(line.Name, 14, bold: true);
                title.TextTrimming = TextTrimming.CharacterEllipsis;
                names.Children.Add(title);
                names.Children.Add(Label($"Satılan (kalan) {line.Quantity} · {Format(line.UnitPrice)} / birim", 12, Muted));
                Grid.SetColumn(names, 1);
                grid.Children.Add(names);
                if (picked[key] > 0 && type != "İptal")
                {
                    var stepper = Stepper(picked[key], value => { picked[key] = value; Render(); }, 1, line.Quantity);
                    Grid.SetColumn(stepper, 2);
                    grid.Children.Add(stepper);
                }
                var amount = Label(picked[key] > 0 ? "−" + Format(line.UnitPrice * picked[key]) : "", 14, "#DC2626", true);
                amount.HorizontalAlignment = HorizontalAlignment.Right;
                amount.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(amount, 3);
                grid.Children.Add(amount);
                returnList.Children.Add(new Border
                {
                    Child = grid, Padding = new Thickness(Gap, 10, Gap, 10), BorderBrush = Paint("#F0F1F4"),
                    BorderThickness = new Thickness(0, 0, 0, i == remaining.Count - 1 ? 0 : 1)
                });
            }

            givenList.Children.Clear();
            if (given.Count == 0)
                givenList.Children.Add(new Border { Child = Label("Yerine verilecek ürünü yukarıdan ekle.", 13, Muted), Padding = new Thickness(Gap, 14, Gap, 14) });
            for (var i = 0; i < given.Count; i++)
            {
                var index = i;
                var line = given[i];
                var row = new DockPanel();
                var remove = Act("", () => { given.RemoveAt(index); Render(); }, "GhostButton", "");
                remove.Width = 34;
                remove.Foreground = Paint("#DC2626");
                AutomationProperties.SetName(remove, $"{line.Name} kaldır");
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                var amount = Label("+" + Format(line.UnitPrice * line.Quantity), 14, "#15803D", true);
                amount.VerticalAlignment = VerticalAlignment.Center;
                amount.Margin = new Thickness(SmallGap, 0, SmallGap, 0);
                DockPanel.SetDock(amount, Dock.Right);
                row.Children.Add(amount);
                var stepper = Stepper(line.Quantity, value => { given[index] = line with { Quantity = value }; Render(); });
                DockPanel.SetDock(stepper, Dock.Right);
                row.Children.Add(stepper);
                var title = Label(line.Name, 14, bold: true);
                title.VerticalAlignment = VerticalAlignment.Center;
                title.TextTrimming = TextTrimming.CharacterEllipsis;
                row.Children.Add(title);
                givenList.Children.Add(new Border
                {
                    Child = row, Padding = new Thickness(Gap, 8, 8, 8), BorderBrush = Paint("#F0F1F4"),
                    BorderThickness = new Thickness(0, 0, 0, index == given.Count - 1 ? 0 : 1)
                });
            }

            var returned = Sum(Returned());
            returnedValue.Text = "−" + Format(returned);
            givenValue.Text = "+" + Format(Sum(given));
            var difference = Difference();
            differenceValue.Text = difference == 0 ? Format(0) : difference < 0 ? $"Müşteriye ödenecek {Format(-difference)}" : $"Müşteriden alınacak {Format(difference)}";
            differenceValue.Foreground = Paint(difference < 0 ? "#DC2626" : difference > 0 ? "#15803D" : Ink);
            var selected = method.SelectedItem as string;
            var methods = TenderMethods.Where(m => m != "Vadeli" || sale.Value.Account.Length > 0)
                .Select(m => m == "Vadeli" ? "Cari hesaba işle" : MethodName(m)).ToList();
            method.ItemsSource = methods;
            var original = sale.Value.Payments.FirstOrDefault()?.Method ?? "Nakit";
            method.SelectedItem = selected is not null && methods.Contains(selected) ? selected
                : original == "Vadeli" && methods.Contains("Cari hesaba işle") ? "Cari hesaba işle" : MethodName(original == "Vadeli" ? "Nakit" : original);
            methodField.Visibility = difference == 0 ? Visibility.Hidden : Visibility.Visible;
        }

        void AddGiven()
        {
            var product = ResolveProduct(productBox);
            var quantity = Quantity(count);
            void Put(string? variant)
            {
                var line = LineFor(product, quantity);
                if (variant is not null) line = line with { Name = $"{product.Name} · {variant}" };
                var existing = given.FindIndex(l => LineKey(l) == LineKey(line));
                if (existing >= 0) given[existing] = given[existing] with { Quantity = given[existing].Quantity + quantity };
                else given.Add(line);
                productBox.Text = "";
                count.Text = "1";
                Render();
            }
            if (OptionsOf(product).Length > 0) PickVariant(product, Put);
            else Put(null);
        }
        render = Render;
        addGiven = AddGiven;
        Render();

        var modal = ShowModal("Sipariş iptal / iade / değişim", root, 780, 860,
            $"{sale.Date:dd.MM.yyyy HH:mm} · {Format(Sum(sale.Value.Lines))}" + (sale.Value.Account.Length > 0 ? $" · {AccountName(sale.Value.Account)}" : ""));
        Append(modal.Footer, Act("Vazgeç", modal.Close));
        Append(modal.Footer, Act("Onayla", () =>
        {
            var back = Returned();
            if (back.Count == 0 && given.Count == 0) throw new InvalidOperationException("İade edilecek ürün seç.");
            if (type == "Değişim" && given.Count == 0) throw new InvalidOperationException("Değişim için yerine verilecek ürünü ekle.");
            if (type == "Değişim" && back.Count == 0) throw new InvalidOperationException("Değişimde iade alınacak ürünü seç.");
            foreach (var group in given.GroupBy(l => l.Code, StringComparer.OrdinalIgnoreCase))
            {
                var returning = back.Where(l => l.Code.Equals(group.Key, StringComparison.OrdinalIgnoreCase)).Sum(l => l.Quantity);
                if (stock.GetValueOrDefault(group.Key).Shop + returning < group.Sum(l => l.Quantity))
                    throw new InvalidOperationException($"Dükkân stoku yetersiz: {ProductName(group.Key)}");
            }
            var difference = Difference();
            Payment[] settlement = [];
            if (difference != 0)
            {
                var choice = method.SelectedItem as string ?? throw new InvalidOperationException("Ödeme yöntemini seç.");
                var code = choice == "Cari hesaba işle" ? "Vadeli" : TenderMethods.First(m => MethodName(m) == choice);
                settlement = [new Payment(code, difference, "")];
            }
            Save("return", new SaleReturn(sale.Id, type, back.ToArray(), given.ToArray(), settlement, reason.Text.Trim()));
            modal.Close();
            Toast(type switch
            {
                "İptal" => "Sipariş iptal edildi.",
                "Değişim" => "Değişim kaydedildi.",
                _ => "İade kaydedildi."
            } + (difference < 0 ? $" Müşteriye {Format(-difference)} ödenecek." : difference > 0 ? $" Müşteriden {Format(difference)} alınacak." : ""));
        }, "WarningButton", ""));
    }

    private void PrintSale(Guid saleId)
    {
        var sale = sales.Single(s => s.Id == saleId);
        var document = new FlowDocument { PagePadding = new Thickness(45), FontFamily = FontFamily };
        document.Blocks.Add(new Paragraph(new Bold(new Run(store.Setting("company", "Aden")))) { FontSize = 24 });
        document.Blocks.Add(new Paragraph(new Run($"SATIŞ ÖZETİ · Mali belge yerine geçmez\n{sale.Date:g}\n{saleId}")));
        foreach (var line in sale.Value.Lines)
            document.Blocks.Add(new Paragraph(new Run($"{line.Name} × {line.Quantity}   {Format(line.UnitPrice * line.Quantity)}")));
        document.Blocks.Add(new Paragraph(new Bold(new Run($"Toplam {Format(Sum(sale.Value.Lines))}"))));
        foreach (var payment in sale.Value.Payments)
            document.Blocks.Add(new Paragraph(new Run($"{MethodName(payment.Method)}   {Format(payment.Amount)}   {payment.Reference}")));
        foreach (var item in returns.Where(r => r.Value.SaleId == saleId))
        {
            document.Blocks.Add(new Paragraph(new Bold(new Run($"{item.Value.Type} · {item.Date:g}"))));
            foreach (var line in item.Value.Returned)
                document.Blocks.Add(new Paragraph(new Run($"İade: {line.Name} × {line.Quantity}   −{Format(line.UnitPrice * line.Quantity)}")));
            foreach (var line in item.Value.Given)
                document.Blocks.Add(new Paragraph(new Run($"Verilen: {line.Name} × {line.Quantity}   {Format(line.UnitPrice * line.Quantity)}")));
            foreach (var payment in item.Value.Payments)
                document.Blocks.Add(new Paragraph(new Run($"{MethodName(payment.Method)}   {Format(payment.Amount)}")));
        }
        Print(document, "Aden satış özeti");
    }
}
