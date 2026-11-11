using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;

namespace Aden;

/// <summary>Depo ve stok: mal kabul (tedarikçi, ödeme durumu, vade), aktarım, stok düzeltme ve tedarikçiye iade.</summary>
public sealed partial class MainWindow
{
    private sealed record StockRow(string Code, string Name, string Unit, long Warehouse, long Shop, long Overall, int Minimum, long Refill, string Durum);
    private sealed record MoveRow(Guid Id, DateTime Tarih, string Tür, string Kod, string Ürün, string Depo, string Dükkân,
        string BirimAlış, string Tutar, string Tedarikçi, string Ödeme, string Belge);
    private sealed record SupplierReturnRow(Guid Id, DateTime Tarih, string No, string Tedarikçi, string Kaynak, string Ürünler, string Tutar, string Neden);

    private const string OtherSupplier = "Diğer (elle yaz)";

    private void AddStock()
    {
        var body = AddPage("Depo ve stok", "Mal kabul, dükkâna aktarım, stok düzeltme ve tedarikçi iadeleri", "", out var actions);
        var page = pages[^1].Content;
        var tools = new DockPanel();
        var lowStock = Label("", 13, "#B45309", true);
        lowStock.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(lowStock, Dock.Right);
        tools.Children.Add(lowStock);
        var searchHost = SearchField("Kod, ürün adı veya barkod ara", out var search);
        searchHost.Width = 360;
        searchHost.HorizontalAlignment = HorizontalAlignment.Left;
        scanTargets[page] = search;
        tools.Children.Add(searchHost);
        var grid = Table();
        Columns(grid, ("Kod", "Code", 0.9), ("Ürün", "Name", 2), ("Birim", "Unit", 0.6), ("Depo", "Warehouse", 0.6), ("Dükkân", "Shop", 0.6),
            ("Toplam", "Overall", 0.6), ("Raf alt sınırı", "Minimum", 0.8), ("Rafa aktarılmalı", "Refill", 0.9), ("Durum", "Durum", 0.9));
        object[] Current()
        {
            var rows = products.Values.OrderBy(p => p.Name, StringComparer.Create(culture, true)).Select(p =>
            {
                var (warehouse, shop) = stock.GetValueOrDefault(p.Code);
                return (Product: p, Row: new StockRow(p.Code, p.Name, p.Unit, warehouse, shop, warehouse + shop, p.Minimum,
                    Math.Min(Math.Max(warehouse, 0), Math.Max(p.Minimum - shop, 0)),
                    shop <= 0 ? "Stok yok" : IsLow(p) ? "Stok azaldı" : ""));
            }).ToArray();
            var low = rows.Count(r => r.Row.Refill > 0);
            lowStock.Text = low > 0 ? $"{low} üründe rafa aktarım gerekli" : "";
            var term = search.Text.Trim();
            return rows.Where(r => Matches(term, r.Row.Code, r.Row.Name, HasBarcode(r.Product) ? r.Product.Barcode : "")).Select(r => (object)r.Row).ToArray();
        }
        views.Add((grid, Current));
        search.TextChanged += (_, _) => grid.ItemsSource = Current();
        search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (grid.Items.Count == 1) { grid.SelectedIndex = 0; Run(() => StockDetail(((StockRow)grid.Items[0]).Code)); }
        };

        var moves = Table();
        moves.AutoGenerateColumns = false;
        foreach (var (title, path, weight) in new[]
        {
            ("Tarih", nameof(MoveRow.Tarih), 1.0), ("Tür", nameof(MoveRow.Tür), 0.8), ("Kod", nameof(MoveRow.Kod), 0.7),
            ("Ürün", nameof(MoveRow.Ürün), 1.6), ("Depo", nameof(MoveRow.Depo), 0.5), ("Dükkân", nameof(MoveRow.Dükkân), 0.5),
            ("Birim alış", nameof(MoveRow.BirimAlış), 0.8), ("Tutar", nameof(MoveRow.Tutar), 0.8), ("Tedarikçi", nameof(MoveRow.Tedarikçi), 1.2),
            ("Ödeme", nameof(MoveRow.Ödeme), 1.3), ("Belge / not", nameof(MoveRow.Belge), 1)
        })
            moves.Columns.Add(new DataGridTextColumn
            {
                Header = title, Width = new DataGridLength(weight, DataGridLengthUnitType.Star),
                Binding = new System.Windows.Data.Binding(path) { StringFormat = path == nameof(MoveRow.Tarih) ? "dd.MM.yyyy HH:mm" : null }
            });
        views.Add((moves, () => movements.AsEnumerable().Reverse().Select(MoveRowOf).Cast<object>().ToArray()));
        OnOpen(moves, () =>
        {
            if (moves.SelectedItem is MoveRow row && movements.FirstOrDefault(m => m.Id == row.Id) is { } move) EditMovement(move);
        });
        var moveHint = Label("Bir hareketi düzeltmek için üzerine çift tıkla.", 12, Muted);
        var returnsGrid = Table();
        views.Add((returnsGrid, () => supplierReturns.AsEnumerable().Reverse().Select(r => (object)new SupplierReturnRow(r.Id, r.Date, r.Value.Number,
            SupplierReturnName(r.Value), r.Value.Source, string.Join(", ", r.Value.Lines.Select(l => $"{l.Name} × {l.Quantity}")),
            Format(r.Value.Lines.Sum(l => (long)l.Quantity * l.UnitCost)), r.Value.Reason)).ToArray()));
        returnsGrid.AutoGeneratingColumn += (_, e) => { if (e.PropertyName == nameof(SupplierReturnRow.Id)) e.Cancel = true; };
        OnOpen(returnsGrid, () =>
        {
            if (returnsGrid.SelectedItem is SupplierReturnRow row && supplierReturns.FirstOrDefault(r => r.Id == row.Id) is { } item) ExportSupplierReturn(item);
        });
        var returnsHint = Label("İade formunu PDF olarak almak için satıra çift tıkla.", 12, Muted);
        body.Children.Add(Segments(out _, ("Mevcut stok", TableCard(grid, tools)), ("Stok hareketleri", TableCard(moves, moveHint)),
            ("Tedarikçi iadeleri", TableCard(returnsGrid, returnsHint))));

        string? SelectedCode() => (grid.SelectedItem as StockRow)?.Code;
        Append(actions, Act("Excel / CSV", () => ShowDataTransfer("Stoklar"), glyph: ""));
        Append(actions, Act("Tedarikçiye iade", () => SupplierReturnForm(SelectedCode()), "DangerButton", ""));
        Append(actions, Act("Dükkâna aktar", () => Transfer(SelectedCode(), null), "PrimaryButton", ""));
        Append(actions, Act("Mal kabul", () => Receive(SelectedCode(), null), "SuccessButton", ""));
        OnOpen(grid, () => { if (SelectedCode() is { } code) StockDetail(code); });
    }

    private string SupplierReturnName(SupplierReturn value) =>
        value.Account.Length > 0 && accounts.TryGetValue(value.Account, out var account) ? account.Name : value.Supplier;

    private MoveRow MoveRowOf(MoveRecord move)
    {
        var value = move.Value;
        var receipt = IsReceipt(value);
        var kind = value.Type == "Sayım" ? "Sayım düzeltme" : receipt ? "Mal kabul" : "Dükkâna aktarım";
        string payment = "";
        if (receipt && value.Status.Length > 0)
            payment = value.Status switch
            {
                "Parçalı" => $"Parçalı · {Format(value.Paid)} ödendi",
                "Vadeli" => $"Vadeli · {value.Term} ay × {Format(ReceiptTotal(value) / Math.Max(1, value.Term))}",
                _ => value.Status
            } + (value.Method.Length > 0 && value.Status != "Borç" ? $" · {value.Method}" : "");
        return new MoveRow(move.Id, move.Date, kind + (move.Edited ? " (düzeltildi)" : ""), value.Code, ProductName(value.Code),
            value.Warehouse.ToString("+#;-#;0", culture), value.Shop.ToString("+#;-#;0", culture),
            receipt ? Format(value.UnitCost) : "", receipt ? Format(ReceiptTotal(value)) : "", receipt ? SupplierName(value) : "",
            payment, value.Reference);
    }

    private sealed record SupplierChoice(string Code, string Label);

    /// <summary>Tedarikçi seçici: Tedarikçi tipindeki cariler, diğer cariler ve "Diğer (elle yaz)".</summary>
    private (FrameworkElement View, Func<(string Account, string Name)> Read, Func<bool> IsOther) SupplierPicker(string account, string name)
    {
        var box = new ComboBox { DisplayMemberPath = nameof(SupplierChoice.Label), MaxDropDownHeight = 340, IsEditable = false };
        var items = accounts.Values.Where(a => a.Type == "Tedarikçi").OrderBy(a => a.Name, StringComparer.Create(culture, true))
            .Select(a => new SupplierChoice(a.Code, $"{a.Name} · {a.Code}")).ToList();
        if (account.Length > 0 && accounts.TryGetValue(account, out var existing) && existing.Type != "Tedarikçi")
            items.Insert(0, new SupplierChoice(existing.Code, $"{existing.Name} · {existing.Code} ({existing.Type})"));
        items.Insert(0, new SupplierChoice("", "— Tedarikçi seçilmedi —"));
        items.Add(new SupplierChoice("*", OtherSupplier));
        box.ItemsSource = items;
        var other = Text(account.Length == 0 ? name : "");
        other.MaxLength = 200;
        var otherHost = Hinted(other, "Tedarikçi adı");
        otherHost.Margin = new Thickness(0, SmallGap, 0, 0);
        box.SelectedItem = account.Length > 0 ? items.FirstOrDefault(i => i.Code.Equals(account, StringComparison.OrdinalIgnoreCase)) ?? items[0]
            : name.Length > 0 ? items[^1] : items[0];
        void Toggle() => otherHost.Visibility = (box.SelectedItem as SupplierChoice)?.Code == "*" ? Visibility.Visible : Visibility.Collapsed;
        box.SelectionChanged += (_, _) => { Toggle(); if (otherHost.Visibility == Visibility.Visible) other.Focus(); };
        Toggle();
        var view = new StackPanel();
        view.Children.Add(box);
        view.Children.Add(otherHost);
        var hint = Label(items.Count <= 2 ? "Kayıtlı tedarikçi yok. Cariler sayfasında cari tipini \"Tedarikçi\" seç veya \"Diğer\" ile yaz." : "", 12, Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        if (hint.Text.Length > 0) { hint.Margin = new Thickness(0, 6, 0, 0); view.Children.Add(hint); }
        AutomationProperties.SetName(box, "Tedarikçi");
        return (view, () => (box.SelectedItem as SupplierChoice)?.Code switch
        {
            "*" => ("", other.Text.Trim()),
            null or "" => ("", ""),
            var code => (code, "")
        }, () => (box.SelectedItem as SupplierChoice)?.Code == "*");
    }

    /// <summary>"Diğer" ile yazılan tedarikçiyi cari olarak eklemeyi sorar; sonra devam eder.</summary>
    private void ResolveSupplier(string account, string name, Action<string, string> proceed)
    {
        if (account.Length > 0 || name.Length == 0) { proceed(account, name); return; }
        var existing = accounts.Values.FirstOrDefault(a => string.Compare(a.Name, name, culture, System.Globalization.CompareOptions.IgnoreCase) == 0);
        if (existing is not null) { proceed(existing.Code, ""); return; }
        var message = Label($"\"{name}\" kayıtlı bir cari değil. Tedarikçi tipinde cari olarak eklersen borç/alacak takibi yapılır.", 13, "#374151");
        message.TextWrapping = TextWrapping.Wrap;
        var ask = ShowModal("Cari olarak eklensin mi?", message, 480);
        Append(ask.Footer, Act("Hayır, sadece adı yaz", () => { ask.Close(); Run(() => proceed("", name)); }));
        Append(ask.Footer, Act("Evet, cari ekle", () =>
        {
            var code = NextAccountCode();
            Save("account", new Account(code, name, "", false, "Tedarikçi") with { Contact = "" });
            ask.Close();
            Toast($"\"{name}\" tedarikçi carisi olarak eklendi · {code}");
            Run(() => proceed(code, ""));
        }, "SuccessButton", ""));
    }

    private void Receive(string? code, MoveRecord? editing)
    {
        if (products.Count == 0) throw new InvalidOperationException("Önce Ürünler sayfasından ürün ekle.");
        var old = editing?.Value;
        var product = ProductBox(old?.Code ?? code);
        if (old is not null) product.IsEnabled = false;
        var count = Text((old?.Warehouse ?? 1).ToString(culture));
        var cost = Text(old is null ? "0,00" : (old.UnitCost / 100m).ToString("N2", culture));
        var reference = Text(old?.Reference ?? "");
        reference.MaxLength = 200;
        var exclusive = new CheckBox { Content = "KDV hariç fiyat gireceğim", FontSize = 12, MinHeight = 0, IsChecked = old?.NetCost is not null };
        var netCost = Text(old?.NetCost is long n0 ? (n0 / 100m).ToString("N2", culture) : "");
        var vat = VatBox(old?.Vat ?? DefaultVat);
        AutomationProperties.SetName(netCost, "KDV hariç birim alış fiyatı");
        var netRow = Form(2, F(Field("KDV hariç birim alış fiyatı (TL)", netCost)), F(Field("KDV oranı (%)", vat)));
        netRow.Margin = new Thickness(0, 12, 0, 0);
        var hint = Label("", 12, Muted);
        hint.Margin = new Thickness(0, SmallGap, 0, 0);
        hint.TextWrapping = TextWrapping.Wrap;
        var costField = Field("Birim alış fiyatı (KDV dahil, TL)", cost, aside: exclusive);

        var supplier = SupplierPicker(old?.Account ?? "", old?.Supplier ?? "");
        var status = old?.Status is { Length: > 0 } s0 ? s0 : "Ödendi";
        Action recalc = () => { };
        var statusBar = new StackPanel { Orientation = Orientation.Horizontal };
        var statusButtons = new Dictionary<string, Button>();
        foreach (var option in EntryValidation.PurchaseStatuses)
        {
            var button = new Button { Content = option == "Parçalı" ? "Parçalı ödendi" : option, Padding = new Thickness(14, 0, 14, 0) };
            if (statusBar.Children.Count > 0) button.Margin = new Thickness(4, 0, 0, 0);
            button.Click += (_, _) => { status = option; recalc(); };
            statusBar.Children.Add(button);
            statusButtons[option] = button;
        }
        var method = new ComboBox { ItemsSource = EntryValidation.PurchaseMethods };
        method.SelectedItem = old?.Method is { Length: > 0 } m0 ? m0 : EntryValidation.PurchaseMethods[0];
        var paid = Text(old is { Status: "Parçalı" } ? (old.Paid / 100m).ToString("N2", culture) : "");
        var term = Text(old is { Term: > 0 } ? old.Term.ToString(culture) : "3");
        var paidField = Field("Peşin ödenen tutar (TL)", paid, true);
        var termField = Field("Vade süresi (ay)", term, true);
        var methodField = Field("Ödeme tipi", method, true);
        var payHint = Label("", 12, Muted);
        payHint.TextWrapping = TextWrapping.Wrap;
        payHint.Margin = new Thickness(0, SmallGap, 0, 0);

        long Gross()
        {
            if (exclusive.IsChecked == true && TryMoney(netCost.Text, out var net) && int.TryParse(vat.Text.Trim().TrimStart('%'), out var rate) && rate is >= 0 and <= 100)
                return WithVat(net, rate);
            return TryMoney(cost.Text, out var gross) ? gross : 0;
        }

        void Recalculate()
        {
            netRow.Visibility = exclusive.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            cost.IsReadOnly = exclusive.IsChecked == true;
            if (exclusive.IsChecked == true)
            {
                if (netCost.Text.Trim().Length > 0 && TryMoney(netCost.Text, out var net) &&
                    int.TryParse(vat.Text.Trim().TrimStart('%'), out var rate) && rate is >= 0 and <= 100)
                {
                    var gross = WithVat(net, rate);
                    cost.Text = (gross / 100m).ToString("N2", culture);
                    hint.Text = $"{Format(net)} + %{rate} KDV ({Format(gross - net)}) = {Format(gross)} alış fiyatı (KDV dahil)";
                }
                else hint.Text = "KDV hariç fiyatı ve KDV oranını gir; KDV dahil alış fiyatı otomatik hesaplanır.";
            }
            else hint.Text = "";
            foreach (var (key, button) in statusButtons)
                button.Style = (Style)FindResource(key == status ? "SegmentActiveButton" : "SegmentButton");
            paidField.Visibility = status == "Parçalı" ? Visibility.Visible : Visibility.Collapsed;
            termField.Visibility = status == "Vadeli" ? Visibility.Visible : Visibility.Collapsed;
            methodField.Visibility = status == "Borç" ? Visibility.Collapsed : Visibility.Visible;
            var total = int.TryParse(count.Text, out var q) && q > 0 ? q * Gross() : 0;
            payHint.Text = status switch
            {
                "Ödendi" => $"Toplam {Format(total)} peşin ödendi; alım ödemesi olarak giderlere yazılır.",
                "Borç" => $"Toplam {Format(total)} tedarikçiye borç olarak yazılır. Ödemeyi Cariler sayfasından \"Ödeme yap\" ile kaydet.",
                "Parçalı" => TryMoney(paid.Text, out var p) && paid.Text.Trim().Length > 0
                    ? $"{Format(p)} peşin ödendi, kalan {Format(Math.Max(0, total - p))} tedarikçiye borç olarak yazılır."
                    : "Peşin ödenen tutarı gir; kalanı tedarikçiye borç olarak yazılır.",
                _ => int.TryParse(term.Text, out var months) && months is > 0 and <= 120
                    ? $"Toplam {Format(total)}, {months} ay boyunca aylık {Format(total / months)} olarak giderlere yansır (ilk taksit 1 ay sonra)."
                    : "Vade süresini 1–120 ay arasında gir."
            };
        }
        recalc = Recalculate;
        exclusive.Checked += (_, _) => { Recalculate(); netCost.Focus(); };
        exclusive.Unchecked += (_, _) => Recalculate();
        foreach (var box in new[] { netCost, count, cost, paid, term }) box.TextChanged += (_, _) => Recalculate();
        vat.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Recalculate()));
        var costPanel = new StackPanel();
        costPanel.Children.Add(costField);
        costPanel.Children.Add(netRow);
        costPanel.Children.Add(hint);

        var root = new StackPanel();
        root.Children.Add(Section("Ürün"));
        root.Children.Add(Form(2, F(Field("Ürün", product, true), 2), F(Field("Adet", count, true)), F(Field("Belge / irsaliye no", reference)), F(costPanel, 2)));
        root.Children.Add(Section("Tedarikçi ve ödeme", PagePadding));
        root.Children.Add(Form(2, F(Field("Tedarikçi (cari)", supplier.View), 2),
            F(Field("Ödeme durumu", new Border
            {
                Child = statusBar, Background = Paint("#F3F4F6"), CornerRadius = new CornerRadius(10), Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Left
            }), 2),
            F(methodField), F(paidField), F(termField)));
        root.Children.Add(payHint);
        Recalculate();

        var modal = ShowModal(old is null ? "Mal kabul" : "Mal kabulü düzelt", root, 680, 860,
            old is null ? "Gelen ürünler depo stokuna eklenir. Alış fiyatı varsayılan olarak KDV dahil girilir."
                : $"{editing!.Date:dd.MM.yyyy HH:mm} tarihli kayıt düzeltilir; stok ve cari bakiyesi yeniden hesaplanır.");
        Append(modal.Footer, Act("Vazgeç", modal.Close));
        if (editing is not null) Append(modal.Footer, Act("Kaydı sil", () => DeleteMovement(editing, modal.Close), "DangerButton", ""));
        Append(modal.Footer, Act(old is null ? "Depoya ekle" : "Güncelle", () =>
        {
            var item = old is null ? ResolveProduct(product) : products.TryGetValue(old.Code, out var p) ? p : throw new InvalidOperationException("Ürün silinmiş.");
            var amount = Quantity(count);
            int? rate = null;
            long? net = null;
            if (exclusive.IsChecked == true)
            {
                if (netCost.Text.Trim().Length == 0 || !TryMoney(netCost.Text, out var value))
                    throw new InvalidOperationException("KDV hariç alış fiyatını 0,00 biçiminde gir.");
                rate = ReadVat(vat);
                net = value;
            }
            var unitCost = net is long n ? WithVat(n, rate!.Value) : Money(cost, true);
            var total = (long)amount * unitCost;
            long paidNow = 0;
            var months = 0;
            if (status == "Parçalı")
            {
                paidNow = Money(paid);
                if (paidNow >= total) throw new InvalidOperationException("Parçalı ödemede peşin tutar toplamdan küçük olmalı. Tamamı ödendiyse \"Ödendi\" seç.");
            }
            if (status == "Vadeli" && (!int.TryParse(term.Text, out months) || months is < 1 or > 120))
                throw new InvalidOperationException("Vade süresini 1–120 ay arasında gir.");
            var (account, name) = supplier.Read();
            if (supplier.IsOther() && name.Length == 0) throw new InvalidOperationException("Tedarikçi adını yaz.");
            if (status is "Borç" or "Parçalı" or "Vadeli" && account.Length == 0 && name.Length == 0)
                throw new InvalidOperationException("Borç, parçalı veya vadeli alımda tedarikçi seç.");
            if (old is not null && stock.GetValueOrDefault(item.Code).Warehouse - old.Warehouse + amount < 0)
                throw new InvalidOperationException("Bu düzeltme depo stokunu eksiye düşürür. Önce aktarım/iade kayıtlarını kontrol et.");
            ResolveSupplier(account, name, (accountCode, supplierName) =>
            {
                var record = new Stock(item.Code, amount, 0, unitCost, reference.Text.Trim(), rate, net, accountCode, supplierName, status,
                    status == "Borç" ? "" : (string)method.SelectedItem, paidNow, months);
                if (editing is null) Save("stock", record);
                else Save("stock-revision", new StockRevision(editing.Id, record));
                modal.Close();
                Toast(editing is null ? $"{item.Name} · {amount} {item.Unit.ToLower(culture)} depoya eklendi." : "Mal kabul kaydı düzeltildi.");
            });
        }, old is null ? "SuccessButton" : "PrimaryButton", old is null ? "" : ""));
    }

    private void DeleteMovement(MoveRecord move, Action? closed = null)
    {
        var after = stock.GetValueOrDefault(move.Value.Code);
        if (after.Warehouse - move.Value.Warehouse < 0 || after.Shop - move.Value.Shop < 0)
            throw new InvalidOperationException("Bu kaydı silmek stoku eksiye düşürür (ürünler aktarılmış veya satılmış olabilir). Gerekirse sayım düzeltmesi yap.");
        Confirm("Stok hareketini sil", $"{move.Date:dd.MM.yyyy HH:mm} · {ProductName(move.Value.Code)} hareketi silinecek; stok ve cari bakiyesi yeniden hesaplanır.", "Sil", () =>
        {
            Save("stock-revision", new StockRevision(move.Id, null));
            closed?.Invoke();
            Toast("Stok hareketi silindi.");
        });
    }

    private void EditMovement(MoveRecord move)
    {
        if (IsReceipt(move.Value)) { Receive(move.Value.Code, move); return; }
        if (move.Value.Type == "Sayım") { StockDetail(move.Value.Code); return; }
        Transfer(move.Value.Code, move);
    }

    private void Transfer(string? code, MoveRecord? editing)
    {
        if (products.Count == 0) throw new InvalidOperationException("Önce Ürünler sayfasından ürün ekle.");
        var product = ProductBox(editing?.Value.Code ?? code);
        if (editing is not null) product.IsEnabled = false;
        var count = Text((editing?.Value.Shop ?? 1).ToString(culture));
        var note = Text(editing?.Value.Reference ?? "");
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
        var modal = ShowModal(editing is null ? "Dükkâna aktar" : "Aktarımı düzelt", form, 560, subtitle: "Depodaki ürünler satış rafına (dükkân stokuna) taşınır.");
        Append(modal.Footer, Act("Vazgeç", modal.Close));
        if (editing is not null) Append(modal.Footer, Act("Kaydı sil", () => DeleteMovement(editing, modal.Close), "DangerButton", ""));
        Append(modal.Footer, Act(editing is null ? "Aktar" : "Güncelle", () =>
        {
            var item = ResolveProduct(product);
            var amount = Quantity(count);
            var previous = editing?.Value.Shop ?? 0;
            var current = stock.GetValueOrDefault(item.Code);
            if (current.Warehouse + previous < amount) throw new InvalidOperationException("Depo stoku yetersiz.");
            if (current.Shop - previous + amount < 0) throw new InvalidOperationException("Bu düzeltme dükkân stokunu eksiye düşürür.");
            var record = new Stock(item.Code, -amount, amount, 0, note.Text.Trim());
            if (editing is null) Save("stock", record);
            else Save("stock-revision", new StockRevision(editing.Id, record));
            modal.Close();
            Toast(editing is null ? $"{item.Name} · {amount} adet dükkâna aktarıldı." : "Aktarım düzeltildi.");
        }, "PrimaryButton", ""));
    }

    /// <summary>Ürünün stok kartı: sayım düzeltmesi ve hareketlerinin düzenlenmesi.</summary>
    private void StockDetail(string code)
    {
        if (!products.TryGetValue(code, out var product)) throw new InvalidOperationException("Ürün bulunamadı.");
        var root = new StackPanel();
        var (warehouse, shop) = stock.GetValueOrDefault(code);
        var warehouseBox = Text(warehouse.ToString(culture));
        var shopBox = Text(shop.ToString(culture));
        var note = Text("Sayım düzeltmesi");
        note.MaxLength = 200;
        root.Children.Add(Section("Stok sayımı / düzeltme"));
        var hint = Label("Depo veya dükkân stoku yanlış girildiyse gerçek sayıları yaz; fark \"Sayım düzeltme\" hareketi olarak kaydedilir.", 12, Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 0, 0, SmallGap);
        root.Children.Add(hint);
        root.Children.Add(Form(3, F(Field($"Depo ({product.Unit.ToLower(culture)})", warehouseBox)), F(Field($"Dükkân ({product.Unit.ToLower(culture)})", shopBox)),
            F(Field("Not", note))));
        var apply = Act("Sayımı kaydet", () =>
        {
            if (!int.TryParse(warehouseBox.Text.Trim(), out var w) || w < 0 || !int.TryParse(shopBox.Text.Trim(), out var s) || s < 0)
                throw new InvalidOperationException("Stok sayısını 0 veya pozitif tam sayı olarak gir.");
            var current = stock.GetValueOrDefault(code);
            var dw = w - current.Warehouse;
            var ds = s - current.Shop;
            if (dw == 0 && ds == 0) throw new InvalidOperationException("Sayılar mevcut stokla aynı.");
            Save("stock", new Stock(code, (int)dw, (int)ds, 0, note.Text.Trim(), Type: "Sayım"));
            Toast($"Stok düzeltildi · depo {dw:+#;-#;0}, dükkân {ds:+#;-#;0}");
        }, "PrimaryButton", "");
        apply.HorizontalAlignment = HorizontalAlignment.Right;
        apply.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(apply);

        root.Children.Add(Section("Bu ürünün stok hareketleri", PagePadding));
        var list = new StackPanel();
        root.Children.Add(Card(list));
        void Render()
        {
            list.Children.Clear();
            var (w, s) = stock.GetValueOrDefault(code);
            warehouseBox.Text = w.ToString(culture);
            shopBox.Text = s.ToString(culture);
            var items = movements.Where(m => m.Value.Code.Equals(code, StringComparison.OrdinalIgnoreCase)).Reverse().ToArray();
            if (items.Length == 0) list.Children.Add(new Border { Child = Label("Henüz hareket yok.", 13, Muted), Padding = new Thickness(Gap, 14, Gap, 14) });
            for (var i = 0; i < items.Length; i++)
            {
                var move = items[i];
                var row = MoveRowOf(move);
                var line = new DockPanel();
                var edit = Act("Düzenle", () => EditMovement(move), "GhostButton", "");
                edit.Foreground = Paint("#2563EB");
                DockPanel.SetDock(edit, Dock.Right);
                line.Children.Add(edit);
                var text = new StackPanel();
                text.Children.Add(Label($"{row.Tür} · depo {row.Depo} · dükkân {row.Dükkân}" + (row.Tutar.Length > 0 ? $" · {row.Tutar}" : ""), 13, bold: true));
                text.Children.Add(Label($"{row.Tarih:dd.MM.yyyy HH:mm}" + string.Concat(new[] { row.Tedarikçi, row.Ödeme, row.Belge }.Where(x => x.Length > 0).Select(x => " · " + x)), 12, Muted));
                line.Children.Add(text);
                list.Children.Add(new Border
                {
                    Child = line, Padding = new Thickness(Gap, 8, 8, 8), BorderBrush = Paint("#F0F1F4"),
                    BorderThickness = new Thickness(0, 0, 0, i == items.Length - 1 ? 0 : 1)
                });
            }
        }
        Render();
        var modal = ShowModal($"{product.Name} · stok kartı", root, 720, 820, $"{product.Code} · Depo {warehouse} · Dükkân {shop}");
        refreshers.Add(Render);
        modal.Closed = () => refreshers.Remove(Render);
        Append(modal.Footer, Act("Kapat", modal.Close));
        Append(modal.Footer, Act("Dükkâna aktar", () => Transfer(code, null), glyph: ""));
        Append(modal.Footer, Act("Mal kabul", () => Receive(code, null), "SuccessButton", ""));
    }

    /// <summary>Tedarikçiye hatalı/sorunlu ürün iadesi: stoktan düşer ve iade formu oluşturur.</summary>
    private void SupplierReturnForm(string? code)
    {
        if (products.Count == 0) throw new InvalidOperationException("Önce ürün ekle.");
        var lines = new List<ReturnLine>();
        var lastSupplier = code is null ? null : movements.LastOrDefault(m => IsReceipt(m.Value) && m.Value.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        var supplier = SupplierPicker(lastSupplier?.Value.Account ?? "", lastSupplier?.Value.Supplier ?? "");
        var source = "Depo";
        var sourceBar = new StackPanel { Orientation = Orientation.Horizontal };
        var depot = new Button { Content = "Depodan", Padding = new Thickness(16, 0, 16, 0) };
        var shopButton = new Button { Content = "Dükkândan", Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(4, 0, 0, 0) };
        sourceBar.Children.Add(depot);
        sourceBar.Children.Add(shopButton);
        var product = ProductBox(code);
        var count = Text("1");
        var cost = Text();
        void SuggestCost()
        {
            if (product.SelectedItem is not ProductChoice choice) return;
            var last = movements.LastOrDefault(m => IsReceipt(m.Value) && m.Value.Code.Equals(choice.Code, StringComparison.OrdinalIgnoreCase));
            cost.Text = last is null ? "0,00" : (last.Value.UnitCost / 100m).ToString("N2", culture);
        }
        product.SelectionChanged += (_, _) => SuggestCost();
        SuggestCost();
        var reason = Multiline("", 56);
        reason.MaxLength = 1000;
        var list = new StackPanel();
        var totalRow = SummaryRow("İade toplamı (KDV dahil alış)", out var totalValue, 15, true);

        void Render()
        {
            depot.Style = (Style)FindResource(source == "Depo" ? "SegmentActiveButton" : "SegmentButton");
            shopButton.Style = (Style)FindResource(source == "Dükkân" ? "SegmentActiveButton" : "SegmentButton");
            list.Children.Clear();
            if (lines.Count == 0) list.Children.Add(new Border { Child = Label("İade edilecek ürünleri ekle.", 13, Muted), Padding = new Thickness(Gap, 14, Gap, 14) });
            for (var i = 0; i < lines.Count; i++)
            {
                var index = i;
                var line = lines[i];
                var row = new DockPanel();
                var remove = Act("", () => { lines.RemoveAt(index); Render(); }, "GhostButton", "");
                remove.Width = 34;
                remove.Foreground = Paint("#DC2626");
                AutomationProperties.SetName(remove, $"{line.Name} kaldır");
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                var amount = Label(Format((long)line.Quantity * line.UnitCost), 14, bold: true);
                amount.VerticalAlignment = VerticalAlignment.Center;
                amount.Margin = new Thickness(SmallGap, 0, SmallGap, 0);
                DockPanel.SetDock(amount, Dock.Right);
                row.Children.Add(amount);
                var text = new StackPanel();
                text.Children.Add(Label(line.Name, 14, bold: true));
                var available = source == "Depo" ? stock.GetValueOrDefault(line.Code).Warehouse : stock.GetValueOrDefault(line.Code).Shop;
                text.Children.Add(Label($"{line.Quantity} × {Format(line.UnitCost)} · {source.ToLower(culture)} stoku {available}", 12, line.Quantity > available ? "#DC2626" : Muted));
                row.Children.Add(text);
                list.Children.Add(new Border
                {
                    Child = row, Padding = new Thickness(Gap, 8, 8, 8), BorderBrush = Paint("#F0F1F4"),
                    BorderThickness = new Thickness(0, 0, 0, index == lines.Count - 1 ? 0 : 1)
                });
            }
            totalValue.Text = Format(lines.Sum(l => (long)l.Quantity * l.UnitCost));
        }
        depot.Click += (_, _) => { source = "Depo"; Render(); };
        shopButton.Click += (_, _) => { source = "Dükkân"; Render(); };
        void AddLine()
        {
            var item = ResolveProduct(product);
            var quantity = Quantity(count);
            var unitCost = Money(cost, true);
            var existing = lines.FindIndex(l => l.Code == item.Code && l.UnitCost == unitCost);
            if (existing >= 0) lines[existing] = lines[existing] with { Quantity = lines[existing].Quantity + quantity };
            else lines.Add(new ReturnLine(item.Code, item.Name, quantity, unitCost));
            count.Text = "1";
            Render();
        }
        var addRow = new Grid();
        addRow.ColumnDefinitions.Add(new ColumnDefinition());
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addRow.Children.Add(Field("Ürün", product));
        var countField = Field("Adet", count);
        countField.Margin = new Thickness(SmallGap, 0, 0, 0);
        Grid.SetColumn(countField, 1);
        addRow.Children.Add(countField);
        var costField = Field("Birim alış (TL)", cost);
        costField.Margin = new Thickness(SmallGap, 0, 0, 0);
        Grid.SetColumn(costField, 2);
        addRow.Children.Add(costField);
        var add = Act("Ekle", AddLine, "SuccessButton", "");
        add.VerticalAlignment = VerticalAlignment.Bottom;
        add.Margin = new Thickness(SmallGap, 0, 0, 0);
        Grid.SetColumn(add, 3);
        addRow.Children.Add(add);

        var root = new StackPanel();
        root.Children.Add(Section("Tedarikçi"));
        root.Children.Add(Form(2, F(Field("Tedarikçi (cari)", supplier.View, true)), F(Field("Stoktan düşülecek yer", new Border
        {
            Child = sourceBar, Background = Paint("#F3F4F6"), CornerRadius = new CornerRadius(10), Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Left
        }))));
        root.Children.Add(Section("İade edilecek ürünler", PagePadding));
        root.Children.Add(addRow);
        var listCard = Card(list);
        listCard.Margin = new Thickness(0, Gap, 0, 0);
        root.Children.Add(listCard);
        root.Children.Add(totalRow);
        root.Children.Add(Section("İade nedeni", PagePadding));
        root.Children.Add(Hinted(reason, "Örn. hasarlı geldi, hatalı ürün, son kullanma tarihi geçmiş"));
        Render();

        var number = NextCode("IR-", supplierReturns.Select(r => r.Value.Number));
        var modal = ShowModal("Tedarikçiye iade talebi", root, 760, 860, $"İade no {number} · Ürünler stoktan düşer, tedarikçi carisine alacak yazılır ve iade formu oluşur.");
        Append(modal.Footer, Act("Vazgeç", modal.Close));
        Append(modal.Footer, Act("İadeyi oluştur", () =>
        {
            if (product.Text.Trim().Length > 0 && lines.Count == 0) AddLine();
            if (lines.Count == 0) throw new InvalidOperationException("İade edilecek ürün ekle.");
            foreach (var group in lines.GroupBy(l => l.Code, StringComparer.OrdinalIgnoreCase))
            {
                var available = source == "Depo" ? stock.GetValueOrDefault(group.Key).Warehouse : stock.GetValueOrDefault(group.Key).Shop;
                if (available < group.Sum(l => l.Quantity))
                    throw new InvalidOperationException($"{source} stoku yetersiz: {ProductName(group.Key)} ({available})");
            }
            var (account, name) = supplier.Read();
            if (account.Length == 0 && name.Length == 0) throw new InvalidOperationException("Tedarikçi seç veya adını yaz.");
            ResolveSupplier(account, name, (accountCode, supplierName) =>
            {
                var value = new SupplierReturn(number, accountCode, supplierName.Length > 0 ? supplierName : AccountName(accountCode),
                    lines.ToArray(), source, reason.Text.Trim());
                Save("supplier-return", value);
                modal.Close();
                Toast($"İade {number} oluşturuldu; ürünler stoktan düşüldü.");
                if (supplierReturns.LastOrDefault(r => r.Value.Number == number) is { } created) ExportSupplierReturn(created);
            });
        }, "DangerButton", ""));
    }

    private void ExportSupplierReturn(SupplierReturnRecord item)
    {
        var dialog = new SaveFileDialog
        {
            Title = "İade formunu kaydet", Filter = "PDF belgesi (*.pdf)|*.pdf", DefaultExt = ".pdf", AddExtension = true,
            FileName = $"Aden-Iade-{item.Value.Number}"
        };
        if (dialog.ShowDialog(this) != true) return;
        var supplier = item.Value.Account.Length > 0 && accounts.TryGetValue(item.Value.Account, out var account) ? account : null;
        var total = item.Value.Lines.Sum(l => (long)l.Quantity * l.UnitCost);
        ReportPdf.Save(dialog.FileName, "TEDARİKÇİ İADE FORMU", Company(),
            [
                ("İade no", item.Value.Number), ("Tarih", item.Date.ToString("dd.MM.yyyy HH:mm", culture)),
                ("Tedarikçi", SupplierReturnName(item.Value) + (supplier is null ? "" : $" ({supplier.Code})")),
                ("Stoktan düşülen", item.Value.Source)
            ],
            supplier is null ? "" : ContactSummary(supplier),
            ["Kod", "Ürün", "Adet", "Birim alış", "Tutar"], [0.12, 0.46, 0.1, 0.16, 0.16],
            item.Value.Lines.Select(l => new[] { l.Code, l.Name, l.Quantity.ToString(culture), Format(l.UnitCost), Format((long)l.Quantity * l.UnitCost) }).ToArray(),
            [("Toplam iade tutarı (KDV dahil)", Format(total))],
            "İade nedeni: " + (item.Value.Reason.Length > 0 ? item.Value.Reason : "—"), true);
        Toast("İade formu kaydedildi.");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); } catch { }
    }

    private (string Name, string Contact, string Logo) Company() =>
        (store.Setting("company", "Aden"), store.Setting("companyContact"), store.Setting("quoteLogo"));
}
