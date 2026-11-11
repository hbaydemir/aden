using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;

namespace Aden;

/// <summary>Raporlar: genel bakış, kâr/zarar (ürün, sipariş, dönem) ve KDV hesabı.</summary>
public sealed partial class MainWindow
{
    private void AddReports()
    {
        var body = AddPage("Raporlar", "Satış, kâr/zarar, gelir-gider ve KDV raporları", "", out var actions);
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
        var weekStart = WeekStart(today);
        var monthStart = today.AddDays(1 - today.Day);
        foreach (var (title, from, to) in new[]
        {
            ("Bugün", today, today), ("Bu hafta", weekStart, today), ("Bu ay", monthStart, today),
            ("Geçen ay", monthStart.AddMonths(-1), monthStart.AddDays(-1)), ("Bu yıl", new DateTime(today.Year, 1, 1), today),
            ("Geçen yıl", new DateTime(today.Year - 1, 1, 1), new DateTime(today.Year - 1, 12, 31))
        })
        {
            var button = new Button { Content = title, Padding = new Thickness(14, 0, 14, 0) };
            if (presets.Children.Count > 0) button.Margin = new Thickness(4, 0, 0, 0);
            button.Click += (_, _) => { start.SelectedDate = from; end.SelectedDate = to; };
            presets.Children.Add(button);
            presetButtons.Add((button, from, to));
        }

        UniformGrid KpiRow() => new() { Columns = 4, Margin = new Thickness(0, 0, -Gap, 0) };
        (TextBlock Value, TextBlock Note) Kpi(UniformGrid row, string title, string glyph, string tint)
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
            note.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(note);
            box.Children.Add(text);
            var card = Card(box, new Thickness(Gap));
            card.Margin = new Thickness(0, 0, Gap, Gap);
            row.Children.Add(card);
            return (value, note);
        }
        DockPanel Pane(UIElement top, UIElement content)
        {
            var dock = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);
            dock.Children.Add(content);
            return dock;
        }

        // ── Genel bakış ──
        var overviewKpis = KpiRow();
        var totalKpi = Kpi(overviewKpis, "Net satış (KDV dahil)", "", "#2563EB");
        var cashKpi = Kpi(overviewKpis, "Nakit", "", "#16A34A");
        var cardKpi = Kpi(overviewKpis, "Kredi kartı · Havale/EFT", "", "#2563EB");
        var creditKpi = Kpi(overviewKpis, "Vadeli", "", "#D97706");
        var incomeKpi = Kpi(overviewKpis, "Diğer gelir", "", "#16A34A");
        var expenseKpi = Kpi(overviewKpis, "Gider", "", "#DC2626");
        var purchaseKpi = Kpi(overviewKpis, "Alım ödemeleri", "", "#B45309");
        var averageKpi = Kpi(overviewKpis, "Ortalama sepet", "", "#0891B2");
        var daily = Table();
        var byProduct = Table();
        var ledger = Table();
        var overview = Pane(overviewKpis, Segments(out _, ("Günlük özet", Card(daily)), ("Ürün bazında satış", Card(byProduct)),
            ("Gelir ve gider hareketleri", Card(ledger))));

        // ── Kâr / zarar ──
        var profitKpis = KpiRow();
        var revenueKpi = Kpi(profitKpis, "Net satış (KDV hariç)", "", "#2563EB");
        var costKpi = Kpi(profitKpis, "Satılan malın maliyeti", "", "#B45309");
        var grossKpi = Kpi(profitKpis, "Brüt kâr", "", "#16A34A");
        var netProfitKpi = Kpi(profitKpis, "Net kâr / zarar", "", "#7C3AED");
        var profitProducts = Table();
        var profitOrders = Table();
        var profitDaily = Table();
        var profitWeekly = Table();
        var profitMonthly = Table();
        var profitYearly = Table();
        var profitNote = Label("Maliyet, ürünün mal kabullerindeki KDV hariç ağırlıklı ortalama alış fiyatıdır. Net kâr = brüt kâr + diğer gelir − gider. " +
            "Alım ödemeleri (vade taksitleri dahil) stok maliyeti olduğu için kâr/zarara ayrıca yazılmaz; Genel bakış'ta görünür.", 12, Muted);
        profitNote.TextWrapping = TextWrapping.Wrap;
        profitNote.Margin = new Thickness(0, 0, 0, 12);
        var profitTop = new StackPanel();
        profitTop.Children.Add(profitKpis);
        profitTop.Children.Add(profitNote);
        var profit = Pane(profitTop, Segments(out _, ("Ürün bazında", Card(profitProducts)), ("Sipariş bazında", Card(profitOrders)),
            ("Günlük", Card(profitDaily)), ("Haftalık", Card(profitWeekly)), ("Aylık", Card(profitMonthly)), ("Yıllık", Card(profitYearly))));

        // ── KDV hesabı ──
        var vatKpis = KpiRow();
        var outputKpi = Kpi(vatKpis, "Hesaplanan KDV (satış)", "", "#2563EB");
        var inputKpi = Kpi(vatKpis, "İndirilecek KDV (alış)", "", "#B45309");
        var payableKpi = Kpi(vatKpis, "Ödenecek KDV", "", "#DC2626");
        var baseKpi = Kpi(vatKpis, "Satış matrahı", "", "#0891B2");
        var vatGroup = "Ay";
        Action render = () => { };
        var vatGroupBar = new StackPanel { Orientation = Orientation.Horizontal };
        var vatGroupButtons = new Dictionary<string, Button>();
        foreach (var key in new[] { "Gün", "Ay", "Yıl" })
        {
            var button = new Button { Content = key == "Gün" ? "Günlük" : key == "Ay" ? "Aylık" : "Yıllık", Padding = new Thickness(16, 0, 16, 0) };
            if (vatGroupBar.Children.Count > 0) button.Margin = new Thickness(4, 0, 0, 0);
            button.Click += (_, _) => { vatGroup = key; render(); };
            vatGroupBar.Children.Add(button);
            vatGroupButtons[key] = button;
        }
        var vatPeriods = Table();
        var vatRates = Table();
        var vatNote = Label("Hesaplanan KDV satışlardan (iadeler düşülerek), indirilecek KDV mal kabullerinden (tedarikçi iadeleri düşülerek) hesaplanır. " +
            "Beyanname öncesi mali müşavirinle kontrol et.", 12, Muted);
        vatNote.TextWrapping = TextWrapping.Wrap;
        vatNote.Margin = new Thickness(0, 0, 0, 12);
        var vatTop = new StackPanel();
        vatTop.Children.Add(vatKpis);
        vatTop.Children.Add(vatNote);
        var vatTools = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        vatTools.Children.Add(Hstack(Label("Zaman damgası", 13, Muted, true), new Border
        {
            Child = vatGroupBar, Background = Paint("#E9EBF0"), CornerRadius = new CornerRadius(10), Padding = new Thickness(4)
        }));
        ((TextBlock)((StackPanel)vatTools.Children[0]).Children[0]).VerticalAlignment = VerticalAlignment.Center;
        vatTop.Children.Add(vatTools);
        var vat = Pane(vatTop, Segments(out _, ("Dönem bazında KDV", Card(vatPeriods)), ("Oran bazında KDV", Card(vatRates))));

        body.Children.Add(Segments(out _, ("Genel bakış", overview), ("Kâr / zarar", profit), ("KDV hesabı", vat)));

        bool Range(out DateTime from, out DateTime to)
        {
            from = start.SelectedDate ?? DateTime.MinValue;
            to = end.SelectedDate ?? DateTime.MinValue;
            return start.SelectedDate is not null && end.SelectedDate is not null && to >= from;
        }

        string Percent(long part, long whole) => whole == 0 ? "—" : $"%{part * 100m / whole:0.#}";
        string Share(long part, long whole) => whole == 0 ? "—" : $"%{part * 100m / whole:0.#} pay";
        long Cost(string code) => averageCost.GetValueOrDefault(code);

        void Render()
        {
            foreach (var (button, from0, to0) in presetButtons)
                button.Style = (Style)FindResource(start.SelectedDate == from0 && end.SelectedDate == to0 ? "SegmentActiveButton" : "SegmentButton");
            foreach (var (key, button) in vatGroupButtons)
                button.Style = (Style)FindResource(key == vatGroup ? "SegmentActiveButton" : "SegmentButton");
            if (!Range(out var from, out var to))
            {
                foreach (var grid in new[] { daily, byProduct, ledger, profitProducts, profitOrders, profitDaily, profitWeekly, profitMonthly, profitYearly, vatPeriods, vatRates })
                    grid.ItemsSource = null;
                return;
            }
            bool InRange(DateTime date) => date.Date >= from && date.Date <= to;

            // Satış ve iade hareketleri (iade −1 işaretli)
            var lines = SoldLines().Where(l => InRange(l.Date)).ToArray();
            var sold = sales.Where(s => InRange(s.Date)).ToArray();
            var settled = returns.Where(r => InRange(r.Date)).ToArray();
            var cash = Read<Cash>("cash").Where(c => InRange(c.Entry.CreatedAt.LocalDateTime)).ToArray();
            var purchases = PurchasePayments().Where(p => InRange(p.Date)).ToArray();
            long Method(string method) => sold.SelectMany(s => s.Value.Payments).Where(p => p.Method == method).Sum(p => p.Amount)
                + settled.SelectMany(r => r.Value.Payments).Where(p => p.Method == method).Sum(p => p.Amount);
            long Gross((DateTime Date, Guid Sale, Line Line, int Sign) l) => l.Sign * (long)l.Line.Quantity * l.Line.UnitPrice;
            long Net((DateTime Date, Guid Sale, Line Line, int Sign) l) => l.Sign * (long)l.Line.Quantity * LineNet(l.Line);
            long LineCost((DateTime Date, Guid Sale, Line Line, int Sign) l) => l.Sign * (long)l.Line.Quantity * Cost(l.Line.Code);

            var total = lines.Sum(Gross);
            var income = cash.Where(c => c.Value.Category == "Gelir").Sum(c => c.Value.Amount);
            var expense = -cash.Where(c => c.Value.Category == "Gider").Sum(c => c.Value.Amount);
            var purchaseTotal = purchases.Sum(p => p.Amount);
            totalKpi.Value.Text = Format(total);
            totalKpi.Note.Text = $"{sold.Length} satış" + (settled.Length > 0 ? $" · {settled.Length} iade/değişim" : "");
            cashKpi.Value.Text = Format(Method("Nakit"));
            cashKpi.Note.Text = Share(Method("Nakit"), total);
            cardKpi.Value.Text = Format(Method("Kart") + Method("Havale/EFT"));
            cardKpi.Note.Text = $"Kart {Format(Method("Kart"))} · Havale {Format(Method("Havale/EFT"))}";
            creditKpi.Value.Text = Format(Method("Vadeli"));
            creditKpi.Note.Text = "Cari hesaplara";
            incomeKpi.Value.Text = Format(income);
            incomeKpi.Note.Text = $"{cash.Count(c => c.Value.Category == "Gelir")} kayıt";
            expenseKpi.Value.Text = Format(expense);
            expenseKpi.Value.Foreground = Paint(expense > 0 ? "#DC2626" : Ink);
            expenseKpi.Note.Text = $"{cash.Count(c => c.Value.Category == "Gider")} kayıt";
            purchaseKpi.Value.Text = Format(purchaseTotal);
            purchaseKpi.Note.Text = $"{purchases.Length} ödeme / vade taksiti";
            averageKpi.Value.Text = Format(sold.Length == 0 ? 0 : sold.Sum(s => Sum(s.Value.Lines)) / sold.Length);
            averageKpi.Note.Text = $"{lines.Sum(l => (long)l.Sign * l.Line.Quantity)} ürün (net) satıldı";

            var days = lines.Select(l => l.Date.Date).Concat(cash.Where(c => c.Value.Category is "Gelir" or "Gider").Select(c => c.Entry.CreatedAt.LocalDateTime.Date))
                .Concat(purchases.Select(p => p.Date.Date)).Distinct().OrderByDescending(d => d);
            daily.ItemsSource = days.Select(day =>
            {
                var dayLines = lines.Where(l => l.Date.Date == day).ToArray();
                var daySales = sold.Count(s => s.Date.Date == day);
                long DayMethod(string method) => sold.Where(s => s.Date.Date == day).SelectMany(s => s.Value.Payments).Where(p => p.Method == method).Sum(p => p.Amount)
                    + settled.Where(r => r.Date.Date == day).SelectMany(r => r.Value.Payments).Where(p => p.Method == method).Sum(p => p.Amount);
                var dayCash = cash.Where(c => c.Entry.CreatedAt.LocalDateTime.Date == day).Select(c => c.Value).ToArray();
                var dayTotal = dayLines.Sum(Gross);
                var dayIncome = dayCash.Where(c => c.Category == "Gelir").Sum(c => c.Amount);
                var dayExpense = -dayCash.Where(c => c.Category == "Gider").Sum(c => c.Amount);
                var dayPurchase = purchases.Where(p => p.Date.Date == day).Sum(p => p.Amount);
                return new
                {
                    Tarih = day.ToString("dd.MM.yyyy", culture), Satış = daySales,
                    Nakit = Format(DayMethod("Nakit")), Kart = Format(DayMethod("Kart")), Havale = Format(DayMethod("Havale/EFT")),
                    Vadeli = Format(DayMethod("Vadeli")), Ciro = Format(dayTotal), Gelir = Format(dayIncome), Gider = Format(dayExpense),
                    AlımÖdemesi = Format(dayPurchase), Net = Format(dayTotal + dayIncome - dayExpense - dayPurchase)
                };
            }).ToArray();
            byProduct.ItemsSource = lines.GroupBy(l => l.Line.Code, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Code: g.Key, Quantity: g.Sum(l => (long)l.Sign * l.Line.Quantity), Total: g.Sum(Gross)))
                .OrderByDescending(g => g.Total)
                .Select(g => new { Kod = g.Code, Ürün = ProductName(g.Code), Adet = g.Quantity, Ciro = Format(g.Total), Pay = Share(g.Total, total) })
                .ToArray();
            ledger.ItemsSource = cash.Select(c => (Date: c.Entry.CreatedAt.LocalDateTime, Kind: c.Value.Category, Detail: c.Value.Detail,
                    Account: c.Value.Account.Length > 0 ? AccountName(c.Value.Account) : "", Amount: c.Value.Amount))
                .Concat(purchases.Select(p => (p.Date, Kind: "Alım ödemesi", p.Detail, p.Account, Amount: -p.Amount)))
                .OrderByDescending(x => x.Date)
                .Select(x => new { Tarih = x.Date, Tür = x.Kind, Açıklama = x.Detail, Cari = x.Account, Tutar = Format(x.Amount) }).ToArray();

            // Kâr / zarar
            var revenue = lines.Sum(Net);
            var cogs = lines.Sum(LineCost);
            var grossProfit = revenue - cogs;
            var netProfit = grossProfit + income - expense;
            revenueKpi.Value.Text = Format(revenue);
            revenueKpi.Note.Text = "İade ve iskonto düşülmüş";
            costKpi.Value.Text = Format(cogs);
            var missing = lines.Select(l => l.Line.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count(c => !averageCost.ContainsKey(c));
            costKpi.Note.Text = missing > 0 ? $"{missing} üründe alış maliyeti yok" : "Ortalama alış (KDV hariç)";
            costKpi.Note.Foreground = Paint(missing > 0 ? "#B45309" : Muted);
            grossKpi.Value.Text = Format(grossProfit);
            grossKpi.Value.Foreground = Paint(grossProfit < 0 ? "#DC2626" : "#15803D");
            grossKpi.Note.Text = $"Marj {Percent(grossProfit, revenue)}";
            netProfitKpi.Value.Text = Format(netProfit);
            netProfitKpi.Value.Foreground = Paint(netProfit < 0 ? "#DC2626" : "#15803D");
            netProfitKpi.Note.Text = $"+ gelir {Format(income)} − gider {Format(expense)}";

            profitProducts.ItemsSource = lines.GroupBy(l => l.Line.Code, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Code: g.Key, Quantity: g.Sum(l => (long)l.Sign * l.Line.Quantity), Revenue: g.Sum(Net), Cost: g.Sum(LineCost)))
                .OrderByDescending(g => g.Revenue - g.Cost)
                .Select(g => new
                {
                    Kod = g.Code, Ürün = ProductName(g.Code), Adet = g.Quantity, SatışKdvHariç = Format(g.Revenue),
                    OrtalamaMaliyet = averageCost.ContainsKey(g.Code) ? Format(Cost(g.Code)) : "Maliyet yok",
                    ToplamMaliyet = Format(g.Cost), BrütKâr = Format(g.Revenue - g.Cost), Marj = Percent(g.Revenue - g.Cost, g.Revenue)
                }).ToArray();
            profitOrders.ItemsSource = lines.GroupBy(l => l.Sale)
                .Select(g => (Sale: sales.FirstOrDefault(s => s.Id == g.Key), Revenue: g.Sum(Net), Cost: g.Sum(LineCost), Gross: g.Sum(Gross)))
                .Where(g => g.Sale is not null)
                .OrderByDescending(g => g.Sale!.Date)
                .Select(g => new
                {
                    Tarih = g.Sale!.Date, Cari = g.Sale.Value.Account.Length > 0 ? AccountName(g.Sale.Value.Account) : "Perakende",
                    Ürünler = string.Join(", ", g.Sale.Value.Lines.Select(l => $"{l.Name} × {l.Quantity}")),
                    TutarKdvDahil = Format(g.Gross), SatışKdvHariç = Format(g.Revenue), Maliyet = Format(g.Cost),
                    Kâr = Format(g.Revenue - g.Cost), Marj = Percent(g.Revenue - g.Cost, g.Revenue), Durum = SaleStatus(g.Sale)
                }).ToArray();
            object[] Periods(Func<DateTime, DateTime> key, Func<DateTime, string> label)
            {
                var keys = lines.Select(l => key(l.Date)).Concat(cash.Select(c => key(c.Entry.CreatedAt.LocalDateTime))).Distinct().OrderByDescending(k => k);
                return keys.Select(k =>
                {
                    var group = lines.Where(l => key(l.Date) == k).ToArray();
                    var periodCash = cash.Where(c => key(c.Entry.CreatedAt.LocalDateTime) == k).Select(c => c.Value).ToArray();
                    var r = group.Sum(Net);
                    var c = group.Sum(LineCost);
                    var inc = periodCash.Where(x => x.Category == "Gelir").Sum(x => x.Amount);
                    var exp = -periodCash.Where(x => x.Category == "Gider").Sum(x => x.Amount);
                    return (object)new
                    {
                        Dönem = label(k), Satış = group.Select(l => l.Sale).Distinct().Count(), SatışKdvHariç = Format(r), Maliyet = Format(c),
                        BrütKâr = Format(r - c), Marj = Percent(r - c, r), DiğerGelir = Format(inc), Gider = Format(exp), NetKâr = Format(r - c + inc - exp)
                    };
                }).ToArray();
            }
            profitDaily.ItemsSource = Periods(d => d.Date, d => d.ToString("dd.MM.yyyy ddd", culture));
            profitWeekly.ItemsSource = Periods(WeekStart, d => $"{d:dd.MM} – {d.AddDays(6):dd.MM.yyyy}");
            profitMonthly.ItemsSource = Periods(d => new DateTime(d.Year, d.Month, 1), d => d.ToString("MMMM yyyy", culture));
            profitYearly.ItemsSource = Periods(d => new DateTime(d.Year, 1, 1), d => d.Year.ToString(culture));

            // KDV
            var receipts = movements.Where(m => IsReceipt(m.Value) && InRange(m.Date)).ToArray();
            var backs = supplierReturns.Where(r => InRange(r.Date)).ToArray();
            long OutputVat((DateTime Date, Guid Sale, Line Line, int Sign) l) => Gross(l) - Net(l);
            long InputVat(Stock s) => (long)s.Warehouse * (s.UnitCost - NetCost(s));
            long ReturnVat(SupplierReturn r) => r.Lines.Sum(l => (long)l.Quantity * (l.UnitCost - WithoutVat(l.UnitCost, ProductVat(l.Code))));
            var output = lines.Sum(OutputVat);
            var input = receipts.Sum(m => InputVat(m.Value)) - backs.Sum(r => ReturnVat(r.Value));
            outputKpi.Value.Text = Format(output);
            outputKpi.Note.Text = $"Matrah {Format(revenue)}";
            inputKpi.Value.Text = Format(input);
            inputKpi.Note.Text = $"{receipts.Length} mal kabul";
            payableKpi.Value.Text = Format(Math.Max(0, output - input));
            payableKpi.Note.Text = output - input < 0 ? $"Devreden KDV {Format(input - output)}" : "Hesaplanan − indirilecek";
            baseKpi.Value.Text = Format(revenue);
            baseKpi.Note.Text = $"KDV dahil {Format(total)}";
            Func<DateTime, DateTime> vatKey = vatGroup switch
            {
                "Gün" => d => d.Date,
                "Yıl" => d => new DateTime(d.Year, 1, 1),
                _ => d => new DateTime(d.Year, d.Month, 1)
            };
            string VatLabel(DateTime d) => vatGroup switch
            {
                "Gün" => d.ToString("dd.MM.yyyy ddd", culture),
                "Yıl" => d.Year.ToString(culture),
                _ => d.ToString("MMMM yyyy", culture)
            };
            vatPeriods.ItemsSource = lines.Select(l => vatKey(l.Date)).Concat(receipts.Select(m => vatKey(m.Date))).Concat(backs.Select(r => vatKey(r.Date)))
                .Distinct().OrderByDescending(k => k).Select(k =>
                {
                    var group = lines.Where(l => vatKey(l.Date) == k).ToArray();
                    var o = group.Sum(OutputVat);
                    var i = receipts.Where(m => vatKey(m.Date) == k).Sum(m => InputVat(m.Value)) - backs.Where(r => vatKey(r.Date) == k).Sum(r => ReturnVat(r.Value));
                    var purchaseBase = receipts.Where(m => vatKey(m.Date) == k).Sum(m => (long)m.Value.Warehouse * NetCost(m.Value));
                    return new
                    {
                        Dönem = VatLabel(k), SatışMatrahı = Format(group.Sum(Net)), HesaplananKdv = Format(o), AlışMatrahı = Format(purchaseBase),
                        İndirilecekKdv = Format(i), ÖdenecekKdv = Format(Math.Max(0, o - i)), DevredenKdv = o - i < 0 ? Format(i - o) : ""
                    };
                }).ToArray();
            vatRates.ItemsSource = lines.GroupBy(l => l.Line.Vat ?? 0).OrderBy(g => g.Key).Select(g => new
            {
                KdvOranı = $"%{g.Key}", Matrah = Format(g.Sum(Net)), HesaplananKdv = Format(g.Sum(OutputVat)), KdvDahil = Format(g.Sum(Gross)),
                Pay = Percent(g.Sum(OutputVat), output)
            }).ToArray();
        }
        render = Render;
        refreshers.Add(Render);
        start.SelectedDateChanged += (_, _) => Render();
        end.SelectedDateChanged += (_, _) => Render();

        Append(actions, Act("Excel'e aktar", () =>
        {
            if (!Range(out var from, out var to)) throw new InvalidOperationException("Geçerli bir tarih aralığı seç.");
            var dialog = new SaveFileDialog
            {
                Title = "Raporu kaydet", Filter = "Excel çalışma kitabı (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx", AddExtension = true, FileName = $"Aden-Rapor-{from:yyyyMMdd}-{to:yyyyMMdd}"
            };
            if (dialog.ShowDialog(this) != true) return;
            DataTransfer.SaveTables(dialog.FileName,
            [
                ("Günlük özet", daily.ItemsSource), ("Ürün bazında satış", byProduct.ItemsSource), ("Gelir-gider", ledger.ItemsSource),
                ("Kâr-zarar ürün", profitProducts.ItemsSource), ("Kâr-zarar sipariş", profitOrders.ItemsSource),
                ("Kâr-zarar günlük", profitDaily.ItemsSource), ("Kâr-zarar haftalık", profitWeekly.ItemsSource),
                ("Kâr-zarar aylık", profitMonthly.ItemsSource), ("Kâr-zarar yıllık", profitYearly.ItemsSource),
                ("KDV dönem", vatPeriods.ItemsSource), ("KDV oran", vatRates.ItemsSource)
            ]);
            Toast("Rapor Excel olarak kaydedildi.");
        }, glyph: ""));
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

    private static DateTime WeekStart(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private int ProductVat(string code) => products.TryGetValue(code, out var product) ? VatOf(product) : DefaultVat;
}
