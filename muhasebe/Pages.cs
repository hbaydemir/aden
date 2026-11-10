using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Aden;

public sealed partial class MainWindow
{
    // ───────────── Depo ve stok ─────────────

    private sealed record StockRow(string Code, string Name, long Warehouse, long Shop, long Overall, int Minimum, long Refill);

    private void AddStock()
    {
        var body = AddPage("Depo ve stok", "Mal kabul, dükkâna aktarım ve stok hareketleri", "", out var actions);
        var tools = new DockPanel();
        var lowStock = Label("", 13, "#B45309", true);
        lowStock.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(lowStock, Dock.Right);
        tools.Children.Add(lowStock);
        var searchHost = SearchField("Kod veya ürün adı ara", out var search);
        searchHost.Width = 360;
        searchHost.HorizontalAlignment = HorizontalAlignment.Left;
        tools.Children.Add(searchHost);
        var grid = Table();
        Columns(grid, ("Kod", "Code", 0.9), ("Ürün", "Name", 2), ("Depo", "Warehouse", 0.7), ("Dükkân", "Shop", 0.7),
            ("Toplam", "Overall", 0.7), ("Raf alt sınırı", "Minimum", 0.8), ("Rafa aktarılmalı", "Refill", 0.9));
        object[] Current()
        {
            var rows = products.Values.OrderBy(p => p.Name).Select(p =>
            {
                var (warehouse, shop) = stock.GetValueOrDefault(p.Code);
                return new StockRow(p.Code, p.Name, warehouse, shop, warehouse + shop, p.Minimum,
                    Math.Min(Math.Max(warehouse, 0), Math.Max(p.Minimum - shop, 0)));
            }).ToArray();
            var low = rows.Count(r => r.Refill > 0);
            lowStock.Text = low > 0 ? $"{low} üründe rafa aktarım gerekli" : "";
            return rows.Where(r => Matches(search.Text.Trim(), r.Code, r.Name)).ToArray();
        }
        views.Add((grid, Current));
        search.TextChanged += (_, _) => grid.ItemsSource = Current();
        var moves = View(() => Read<Stock>("stock").Reverse().Select(m => new
        {
            Tarih = m.Entry.CreatedAt.LocalDateTime, Kod = m.Value.Code,
            Ürün = products.TryGetValue(m.Value.Code, out var p) ? p.Name : "",
            Depo = m.Value.Warehouse, Dükkân = m.Value.Shop, BirimAlış = Format(m.Value.UnitCost), Belge = m.Value.Reference
        }).ToArray());
        body.Children.Add(Segments(out _, ("Mevcut stok", TableCard(grid, tools)), ("Stok hareketleri", Card(moves))));

        string? SelectedCode() => (grid.SelectedItem as StockRow)?.Code;
        Append(actions, Act("Dükkâna aktar", () => Transfer(SelectedCode()), "PrimaryButton", ""));
        Append(actions, Act("Mal kabul", () => Receive(SelectedCode()), "SuccessButton", ""));
        OnOpen(grid, () => Transfer(SelectedCode()));

        void Receive(string? code)
        {
            if (products.Count == 0) throw new InvalidOperationException("Önce Ürünler sayfasından ürün ekle.");
            var product = ProductBox(code);
            var count = Text("1");
            var cost = Text("0,00");
            var reference = Text();
            var form = Form(2, F(Field("Ürün", product), 2), F(Field("Adet", count)), F(Field("Birim alış fiyatı (TL)", cost)),
                F(Field("Belge no / tedarikçi", reference), 2));
            var modal = ShowModal("Mal kabul", form, 560, subtitle: "Gelen ürünler depo stokuna eklenir.");
            Append(modal.Footer, Act("Vazgeç", modal.Close));
            Append(modal.Footer, Act("Depoya ekle", () =>
            {
                var item = ResolveProduct(product);
                var amount = Quantity(count);
                Save("stock", new Stock(item.Code, amount, 0, Money(cost, true), Required(reference)));
                modal.Close();
                Toast($"{item.Name} · {amount} adet depoya eklendi.");
            }, "SuccessButton", ""));
        }

        void Transfer(string? code)
        {
            if (products.Count == 0) throw new InvalidOperationException("Önce Ürünler sayfasından ürün ekle.");
            var product = ProductBox(code);
            var count = Text("1");
            var note = Text();
            var available = Label("", 12, Muted);
            available.Margin = new Thickness(0, 6, 0, 0);
            void ShowAvailable() => available.Text = product.SelectedItem is ProductChoice c
                ? $"Depo: {stock.GetValueOrDefault(c.Code).Warehouse} · Dükkân: {stock.GetValueOrDefault(c.Code).Shop}" : "";
            product.SelectionChanged += (_, _) => ShowAvailable();
            ShowAvailable();
            var productField = new StackPanel();
            productField.Children.Add(Field("Ürün", product));
            productField.Children.Add(available);
            var form = Form(2, F(productField, 2), F(Field("Adet", count)), F(Field("Not (isteğe bağlı)", note)));
            var modal = ShowModal("Dükkâna aktar", form, 560, subtitle: "Depodaki ürünler satış rafına (dükkân stokuna) taşınır.");
            Append(modal.Footer, Act("Vazgeç", modal.Close));
            Append(modal.Footer, Act("Aktar", () =>
            {
                var item = ResolveProduct(product);
                var amount = Quantity(count);
                if (stock.GetValueOrDefault(item.Code).Warehouse < amount)
                    throw new InvalidOperationException("Depo stoku yetersiz.");
                Save("stock", new Stock(item.Code, -amount, amount, 0, note.Text.Trim()));
                modal.Close();
                Toast($"{item.Name} · {amount} adet dükkâna aktarıldı.");
            }, "PrimaryButton", ""));
        }
    }

    // ───────────── Raporlar ─────────────

    private void AddReports()
    {
        var body = AddPage("Raporlar", "Satış, gelir ve gider raporları", "", out var actions);
        var start = new DatePicker { SelectedDate = DateTime.Today.AddDays(1 - DateTime.Today.Day), Width = 150 };
        var end = new DatePicker { SelectedDate = DateTime.Today, Width = 150 };

        // Dönem seçimi
        var periodBar = new DockPanel { Margin = new Thickness(0, 0, 0, Gap) };
        DockPanel.SetDock(periodBar, Dock.Top);
        body.Children.Add(periodBar);
        var dates = Hstack(Label("Tarih aralığı", 13, Muted, true), start, Label("–", 13, Muted), end);
        foreach (var child in dates.Children.OfType<TextBlock>()) child.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(dates, Dock.Right);
        periodBar.Children.Add(dates);
        var presets = new StackPanel { Orientation = Orientation.Horizontal };
        periodBar.Children.Add(new Border
        {
            Child = presets, Background = Paint("#E9EBF0"), CornerRadius = new CornerRadius(10), Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Left
        });
        var presetButtons = new List<(Button Button, DateTime From, DateTime To)>();
        var today = DateTime.Today;
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var monthStart = today.AddDays(1 - today.Day);
        foreach (var (title, from, to) in new[]
        {
            ("Bugün", today, today), ("Bu hafta", weekStart, today), ("Bu ay", monthStart, today),
            ("Geçen ay", monthStart.AddMonths(-1), monthStart.AddDays(-1)), ("Bu yıl", new DateTime(today.Year, 1, 1), today)
        })
        {
            var button = new Button { Content = title, Padding = new Thickness(14, 0, 14, 0) };
            if (presets.Children.Count > 0) button.Margin = new Thickness(4, 0, 0, 0);
            button.Click += (_, _) => { start.SelectedDate = from; end.SelectedDate = to; };
            presets.Children.Add(button);
            presetButtons.Add((button, from, to));
        }

        // Özet kartları
        var kpis = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -Gap, 0) };
        DockPanel.SetDock(kpis, Dock.Top);
        body.Children.Add(kpis);
        (TextBlock Value, TextBlock Note) Kpi(string title, string glyph, string tint)
        {
            var box = new DockPanel();
            var icon = new Border
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Background = Paint("#1A" + tint[1..]),
                Child = Glyph(glyph, 16, tint), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0)
            };
            ((TextBlock)icon.Child).HorizontalAlignment = HorizontalAlignment.Center;
            DockPanel.SetDock(icon, Dock.Left);
            box.Children.Add(icon);
            var text = new StackPanel();
            text.Children.Add(Label(title, 12, Muted, true));
            var value = Label("", 20, bold: true);
            value.Margin = new Thickness(0, 4, 0, 0);
            text.Children.Add(value);
            var note = Label("", 12, Muted);
            note.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(note);
            box.Children.Add(text);
            var card = Card(box, new Thickness(Gap));
            card.Margin = new Thickness(0, 0, Gap, Gap);
            kpis.Children.Add(card);
            return (value, note);
        }
        var totalKpi = Kpi("Toplam satış", "", "#2563EB");
        var cashKpi = Kpi("Nakit", "", "#16A34A");
        var cardKpi = Kpi("Kart", "", "#2563EB");
        var creditKpi = Kpi("Vadeli", "", "#D97706");
        var incomeKpi = Kpi("Diğer gelir", "", "#16A34A");
        var expenseKpi = Kpi("Gider", "", "#DC2626");
        var netKpi = Kpi("Net sonuç", "", "#7C3AED");
        var averageKpi = Kpi("Ortalama sepet", "", "#0891B2");

        // Detay tabloları
        var daily = Table();
        var byProduct = Table();
        var ledger = Table();
        var details = Segments(out _, ("Günlük özet", Card(daily)), ("Ürün bazında satış", Card(byProduct)), ("Gelir ve gider hareketleri", Card(ledger)));
        details.Margin = new Thickness(0, SmallGap, 0, 0);
        body.Children.Add(details);

        bool Range(out DateTime from, out DateTime to)
        {
            from = start.SelectedDate ?? DateTime.MinValue;
            to = end.SelectedDate ?? DateTime.MinValue;
            return start.SelectedDate is not null && end.SelectedDate is not null && to >= from;
        }

        void Render()
        {
            foreach (var (button, from0, to0) in presetButtons)
                button.Style = (Style)FindResource(start.SelectedDate == from0 && end.SelectedDate == to0 ? "SegmentActiveButton" : "SegmentButton");
            if (!Range(out var from, out var to))
            {
                daily.ItemsSource = byProduct.ItemsSource = ledger.ItemsSource = null;
                return;
            }
            bool InRange(Entry entry) => entry.CreatedAt.LocalDateTime.Date >= from && entry.CreatedAt.LocalDateTime.Date <= to;
            var sales = Read<Sale>("sale").Where(s => InRange(s.Entry)).ToArray();
            var cash = Read<Cash>("cash").Where(c => InRange(c.Entry)).ToArray();
            long Method(IEnumerable<Sale> items, string method) => items.SelectMany(s => s.Payments).Where(p => p.Method == method).Sum(p => p.Amount);
            var total = sales.Sum(s => Sum(s.Value.Lines));
            var income = cash.Where(c => c.Value.Category == "Gelir").Sum(c => c.Value.Amount);
            var expense = -cash.Where(c => c.Value.Category == "Gider").Sum(c => c.Value.Amount);
            var values = sales.Select(s => s.Value).ToArray();
            totalKpi.Value.Text = Format(total);
            totalKpi.Note.Text = $"{sales.Length} satış";
            cashKpi.Value.Text = Format(Method(values, "Nakit"));
            cashKpi.Note.Text = Share(Method(values, "Nakit"), total);
            cardKpi.Value.Text = Format(Method(values, "Kart"));
            cardKpi.Note.Text = Share(Method(values, "Kart"), total);
            creditKpi.Value.Text = Format(Method(values, "Vadeli"));
            creditKpi.Note.Text = "Cari hesaplara";
            incomeKpi.Value.Text = Format(income);
            incomeKpi.Note.Text = $"{cash.Count(c => c.Value.Category == "Gelir")} kayıt";
            expenseKpi.Value.Text = Format(expense);
            expenseKpi.Value.Foreground = Paint(expense > 0 ? "#DC2626" : Ink);
            expenseKpi.Note.Text = $"{cash.Count(c => c.Value.Category == "Gider")} kayıt";
            var net = total + income - expense;
            netKpi.Value.Text = Format(net);
            netKpi.Value.Foreground = Paint(net < 0 ? "#DC2626" : "#15803D");
            netKpi.Note.Text = "Satış + gelir − gider";
            averageKpi.Value.Text = Format(sales.Length == 0 ? 0 : total / sales.Length);
            averageKpi.Note.Text = $"{values.Sum(s => s.Lines.Sum(l => (long)l.Quantity))} ürün satıldı";

            var days = sales.Select(s => s.Entry.CreatedAt.LocalDateTime.Date)
                .Concat(cash.Where(c => c.Value.Category is "Gelir" or "Gider").Select(c => c.Entry.CreatedAt.LocalDateTime.Date))
                .Distinct().OrderByDescending(d => d);
            daily.ItemsSource = days.Select(day =>
            {
                var daySales = sales.Where(s => s.Entry.CreatedAt.LocalDateTime.Date == day).Select(s => s.Value).ToArray();
                var dayCash = cash.Where(c => c.Entry.CreatedAt.LocalDateTime.Date == day).Select(c => c.Value).ToArray();
                var dayTotal = daySales.Sum(s => Sum(s.Lines));
                var dayIncome = dayCash.Where(c => c.Category == "Gelir").Sum(c => c.Amount);
                var dayExpense = -dayCash.Where(c => c.Category == "Gider").Sum(c => c.Amount);
                return new
                {
                    Tarih = day.ToString("dd.MM.yyyy ddd", culture), Satış = daySales.Length,
                    Nakit = Format(Method(daySales, "Nakit")), Kart = Format(Method(daySales, "Kart")),
                    Vadeli = Format(Method(daySales, "Vadeli")), Ciro = Format(dayTotal),
                    Gelir = Format(dayIncome), Gider = Format(dayExpense), Net = Format(dayTotal + dayIncome - dayExpense)
                };
            }).ToArray();
            byProduct.ItemsSource = values.SelectMany(s => s.Lines).GroupBy(l => l.Code)
                .Select(g => (Code: g.Key, Name: g.Last().Name, Quantity: g.Sum(l => (long)l.Quantity), Total: Sum(g)))
                .OrderByDescending(g => g.Total)
                .Select(g => new { Kod = g.Code, Ürün = g.Name, Adet = g.Quantity, Ciro = Format(g.Total), Pay = Share(g.Total, total) })
                .ToArray();
            ledger.ItemsSource = cash.Reverse().Select(c => new
            {
                Tarih = c.Entry.CreatedAt.LocalDateTime, Tür = c.Value.Category, Açıklama = c.Value.Detail,
                Cari = c.Value.Account, Tutar = Format(c.Value.Amount)
            }).ToArray();
        }
        string Share(long part, long whole) => whole == 0 ? "—" : $"%{part * 100m / whole:0.#} pay";
        refreshers.Add(Render);
        start.SelectedDateChanged += (_, _) => Render();
        end.SelectedDateChanged += (_, _) => Render();

        Append(actions, Act("Excel'e aktar", () =>
        {
            if (!Range(out var from, out var to)) throw new InvalidOperationException("Geçerli bir tarih aralığı seç.");
            var dialog = new SaveFileDialog
            {
                Title = "Satış raporunu kaydet", Filter = "Excel çalışma kitabı (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx", AddExtension = true, FileName = $"Aden-Satis-{from:yyyyMMdd}-{to:yyyyMMdd}"
            };
            if (dialog.ShowDialog(this) != true) return;
            SalesWorkbook.Save(dialog.FileName, Read<Sale>("sale").Where(sale =>
                sale.Entry.CreatedAt.LocalDateTime.Date >= from && sale.Entry.CreatedAt.LocalDateTime.Date <= to));
            Toast("Satış raporu kaydedildi.");
        }, glyph: ""));
        Append(actions, Act("Gelir / gider ekle", AddIncomeExpense, "SuccessButton", ""));

        void AddIncomeExpense()
        {
            var kind = "Gider";
            var toggle = new StackPanel { Orientation = Orientation.Horizontal };
            var typeBar = new Border
            {
                Child = toggle, Background = Paint("#F3F4F6"), CornerRadius = new CornerRadius(10), Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var expenseButton = new Button { Content = "Gider", Width = 110 };
            var incomeButton = new Button { Content = "Gelir", Width = 110, Margin = new Thickness(4, 0, 0, 0) };
            toggle.Children.Add(expenseButton);
            toggle.Children.Add(incomeButton);
            void SetKind(string value)
            {
                kind = value;
                expenseButton.Style = (Style)FindResource(value == "Gider" ? "SegmentActiveButton" : "SegmentButton");
                incomeButton.Style = (Style)FindResource(value == "Gelir" ? "SegmentActiveButton" : "SegmentButton");
            }
            expenseButton.Click += (_, _) => SetKind("Gider");
            incomeButton.Click += (_, _) => SetKind("Gelir");
            SetKind("Gider");
            var detail = Text();
            var amount = Text();
            var form = Form(2, F(Field("Tür", typeBar), 2), F(Field("Kategori / açıklama", detail)), F(Field("Tutar (TL)", amount)));
            var modal = ShowModal("Gelir / gider ekle", form, 560, subtitle: "Kira, fatura, maaş gibi satış dışı hareketleri kaydet.");
            Append(modal.Footer, Act("Vazgeç", modal.Close));
            Append(modal.Footer, Act("Kaydet", () =>
            {
                Save("cash", new Cash(kind, Required(detail), "", (kind == "Gider" ? -1 : 1) * Money(amount)));
                modal.Close();
                Toast($"{kind} kaydedildi.");
            }, "SuccessButton", ""));
        }
    }

    // ───────────── Ayarlar ─────────────

    private Border SettingsCard(string title, string description, UIElement content, params Button[] buttons)
    {
        var stack = new StackPanel();
        stack.Children.Add(Label(title, 15, bold: true));
        var desc = Label(description, 13, Muted);
        desc.TextWrapping = TextWrapping.Wrap;
        desc.Margin = new Thickness(0, 4, 0, 20);
        stack.Children.Add(desc);
        stack.Children.Add(content);
        if (buttons.Length > 0)
        {
            var row = Hstack(buttons);
            row.HorizontalAlignment = HorizontalAlignment.Right;
            row.Margin = new Thickness(0, 20, 0, 0);
            stack.Children.Add(row);
        }
        var card = Card(stack, new Thickness(PagePadding));
        card.Margin = new Thickness(0, 0, 0, Gap);
        return card;
    }

    private void AddSettings()
    {
        var body = AddPage("Ayarlar", "Firma bilgileri, teklif logosu, eşitleme ve güncellemeler", "", out _);
        var panel = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        var scroll = TouchScroll(panel);
        scroll.Margin = new Thickness(0, 0, -PagePadding, 0);
        scroll.Padding = new Thickness(0, 0, PagePadding, 0);
        body.Children.Add(scroll);
        panel.Width = 760;

        var company = Text(store.Setting("company", "Aden"));
        var contact = Multiline(store.Setting("companyContact"));
        panel.Children.Add(SettingsCard("Firma bilgileri", "Teklif ve satış özetlerinde görünür.",
            Form(1, F(Field("Firma unvanı", company)), F(Field("Adres ve iletişim bilgileri", contact))),
            Act("Kaydet", () =>
            {
                store.SaveSetting("company", Required(company));
                store.SaveSetting("companyContact", contact.Text.Trim());
                Toast("Firma bilgileri kaydedildi.");
            }, "SuccessButton", "")));

        var preview = new Image { Height = 56, MaxWidth = 220, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        void ShowLogo()
        {
            var configured = store.Setting("quoteLogo");
            if (configured.Length == 0) { preview.Source = Brand.Wordmark; return; }
            using var stream = new System.IO.MemoryStream(Convert.FromBase64String(configured));
            preview.Source = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        }
        ShowLogo();
        var logoBox = new Border
        {
            Child = preview, Background = Paint("#F9FAFB"), BorderBrush = Paint(LineColor), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(Gap), HorizontalAlignment = HorizontalAlignment.Left
        };
        panel.Children.Add(SettingsCard("Teklif logosu", "PNG veya JPG, en fazla 2 MB. Teklif PDF'i ve çıktısının üst köşesinde yer alır.", logoBox,
            Act("Varsayılana dön", () => { store.SaveSetting("quoteLogo", ""); ShowLogo(); Toast("Varsayılan logo kullanılacak."); }, "DangerButton"),
            Act("Logo seç", () =>
            {
                var picker = new OpenFileDialog { Filter = "Resim|*.png;*.jpg;*.jpeg" };
                if (picker.ShowDialog(this) != true) return;
                var file = new System.IO.FileInfo(picker.FileName);
                if (file.Length > 2_000_000) throw new InvalidOperationException("Logo en fazla 2 MB olabilir.");
                var bytes = System.IO.File.ReadAllBytes(picker.FileName);
                using var image = new System.IO.MemoryStream(bytes);
                BitmapDecoder.Create(image, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                store.SaveSetting("quoteLogo", Convert.ToBase64String(bytes));
                ShowLogo();
                Toast("Teklif logosu güncellendi.");
            }, "PrimaryButton", "")));

        var enabled = new CheckBox { Content = "Sunucu eşitlemesini aç", IsChecked = store.Setting("mode") == "sync" };
        var url = Text(store.Setting("url", "http://127.0.0.1:5080"));
        var password = new PasswordBox { Password = store.Setting("key") };
        var syncForm = new StackPanel();
        syncForm.Children.Add(enabled);
        var syncFields = Form(2, F(Field("Sunucu adresi", url)), F(Field("Eşitleme anahtarı", password)));
        syncFields.Margin = new Thickness(0, Gap, 0, 0);
        syncForm.Children.Add(syncFields);
        void ToggleFields() => syncFields.IsEnabled = enabled.IsChecked == true;
        enabled.Checked += (_, _) => ToggleFields();
        enabled.Unchecked += (_, _) => ToggleFields();
        ToggleFields();
        panel.Children.Add(SettingsCard("Sunucu eşitlemesi", "Kapalıyken uygulama yalnızca bu bilgisayardaki veritabanını kullanır ve ağ isteği göndermez.", syncForm,
            Act("Çalışma modunu kaydet", () =>
            {
                if (syncing) throw new InvalidOperationException("Eşitleme tamamlanınca tekrar dene.");
                var endpoint = url.Text.Trim().TrimEnd('/');
                if (enabled.IsChecked == true)
                {
                    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                        !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) ||
                        uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0 || password.Password.Length < 24)
                        throw new InvalidOperationException("HTTPS adresi veya yerel HTTP adresi ve en az 24 karakter anahtar gir.");
                    store.SaveSyncSettings("sync", endpoint, password.Password);
                    status.Text = "Eşitleme bekleniyor";
                }
                else
                {
                    store.SaveSyncSettings("local", "", "");
                    status.Text = "Yerel mod · Veriler bu bilgisayarda";
                }
                Toast("Çalışma modu kaydedildi.");
            }, "PrimaryButton", "")));

        AddUpdateSettings(panel);

        var note = Label("E-Fatura ve ÖKC sağlayıcısı henüz yapılandırılmadı. Kart kayıtları manuel teyit gerektirir.", 13, "#374151");
        note.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(SettingsCard("Entegrasyonlar", "Mali belge ve ödeme cihazı bağlantıları.", note));
    }
}
