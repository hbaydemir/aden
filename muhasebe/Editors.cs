using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Aden;

public sealed partial class MainWindow
{
    private bool Matches(string term, params string[] fields) => term.Length == 0 ||
        fields.Any(field => culture.CompareInfo.IndexOf(field, term, System.Globalization.CompareOptions.IgnoreCase) >= 0);

    private sealed record ProductChoice(string Code, string Label);
    private sealed record AccountChoice(string Code, string Label);

    /// <summary>Yazarak aranabilen ürün seçici.</summary>
    private ComboBox ProductBox(string? code = null)
    {
        var box = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, StaysOpenOnEdit = true };
        TextSearch.SetTextPath(box, nameof(ProductChoice.Label));
        box.DisplayMemberPath = nameof(ProductChoice.Label);
        box.ItemsSource = products.Values.OrderBy(p => p.Name).Select(p => new ProductChoice(p.Code, $"{p.Name} · {p.Code}")).ToArray();
        if (code is not null) box.SelectedItem = box.Items.Cast<ProductChoice>().FirstOrDefault(c => c.Code == code);
        return box;
    }

    private Product ResolveProduct(ComboBox box) =>
        box.SelectedItem is ProductChoice choice && products.TryGetValue(choice.Code, out var product)
            ? product
            : box.Text.Trim() is { Length: > 0 } text ? Find(text) : throw new InvalidOperationException("Ürün seç.");

    private ComboBox AccountBox(string? code = null)
    {
        var box = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, StaysOpenOnEdit = true };
        TextSearch.SetTextPath(box, nameof(AccountChoice.Label));
        box.DisplayMemberPath = nameof(AccountChoice.Label);
        box.ItemsSource = accounts.Values.OrderBy(a => a.Name).Select(a => new AccountChoice(a.Code, $"{a.Name} · {a.Code}")).ToArray();
        if (code is not null) box.SelectedItem = box.Items.Cast<AccountChoice>().FirstOrDefault(c => c.Code == code);
        return box;
    }

    private Account ResolveAccount(ComboBox box)
    {
        if (box.SelectedItem is AccountChoice choice && accounts.TryGetValue(choice.Code, out var account)) return account;
        var text = box.Text.Trim();
        if (accounts.TryGetValue(text, out account)) return account;
        var matches = accounts.Values.Where(a => Matches(text, a.Name)).Take(2).ToArray();
        return text.Length > 0 && matches.Length == 1 ? matches[0] : throw new InvalidOperationException("Geçerli bir cari seç.");
    }

    private Account? TryAccount(ComboBox box)
    {
        try { return box.Text.Trim().Length == 0 && box.SelectedItem is null ? null : ResolveAccount(box); }
        catch (InvalidOperationException) { return null; }
    }

    private void OnOpen(DataGrid grid, Action open) =>
        grid.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(grid, source) is DataGridRow)
                Run(open);
        };

    // ───────────── Ürünler ─────────────

    private sealed record ProductRow(ImageSource Thumb, string Code, string Name, string Category, string Barcode,
        string Net, string Vat, string Gross, string Discount, string Shop, Visibility Low);

    private void AddProducts()
    {
        var body = AddPage("Ürünler", "Ürün kartları, kategoriler, barkodlar ve satış fiyatları", "", out var actions);
        productsPage = pages[^1].Content;
        var searchHost = SearchField("Kod, ürün adı, kategori veya barkod ara", out var search);
        searchHost.Width = 360;
        scanTargets[pages[^1].Content] = search;
        var lowOnly = false;
        var lowButton = new Button { Style = (Style)FindResource("DangerButton"), Margin = new Thickness(SmallGap, 0, 0, 0), Visibility = Visibility.Collapsed };
        var tools = new DockPanel();
        DockPanel.SetDock(searchHost, Dock.Left);
        tools.Children.Add(searchHost);
        DockPanel.SetDock(lowButton, Dock.Left);
        tools.Children.Add(lowButton);
        tools.Children.Add(new Border());
        var grid = Table();
        lowButton.Click += (_, _) => { lowOnly = !lowOnly; grid.ItemsSource = Filter(); };
        grid.AutoGenerateColumns = false;
        grid.RowHeight = 48;
        var thumb = new FrameworkElementFactory(typeof(Border));
        thumb.SetValue(Border.WidthProperty, 36.0);
        thumb.SetValue(Border.HeightProperty, 36.0);
        thumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        thumb.SetValue(Border.BackgroundProperty, Paint("#F1F2F4"));
        thumb.SetValue(UIElement.ClipToBoundsProperty, true);
        var thumbImage = new FrameworkElementFactory(typeof(Image));
        thumbImage.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(ProductRow.Thumb)));
        thumbImage.SetValue(Image.StretchProperty, Stretch.UniformToFill);
        thumb.AppendChild(thumbImage);
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "", Width = new DataGridLength(56), CellTemplate = new DataTemplate { VisualTree = thumb }
        });
        Columns(grid, ("Kod", "Code", 0.7), ("Ürün", "Name", 1.9), ("Kategori", "Category", 1), ("Barkod", "Barcode", 1.3),
            ("KDV hariç", "Net", 0.8), ("KDV", "Vat", 0.55), ("KDV dahil", "Gross", 0.8), ("İndirimli (dahil)", "Discount", 1.1),
            ("Stok", "Shop", 0.8));
        // Raf sınırı rozeti
        var badge = new FrameworkElementFactory(typeof(Border));
        badge.SetValue(Border.BackgroundProperty, Paint("#FEE2E2"));
        badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        badge.SetValue(Border.PaddingProperty, new Thickness(7, 2, 8, 3));
        badge.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        badge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        badge.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding(nameof(ProductRow.Low)));
        badge.SetValue(FrameworkElement.ToolTipProperty, "Dükkân stoku raf alt sınırında veya altında");
        var badgeRow = new FrameworkElementFactory(typeof(StackPanel));
        badgeRow.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var badgeIcon = new FrameworkElementFactory(typeof(TextBlock));
        badgeIcon.SetValue(TextBlock.TextProperty, "\uE7BA");
        badgeIcon.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
        badgeIcon.SetValue(TextBlock.FontSizeProperty, 11.0);
        badgeIcon.SetValue(TextBlock.ForegroundProperty, Paint("#DC2626"));
        badgeIcon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        badgeRow.AppendChild(badgeIcon);
        var badgeText = new FrameworkElementFactory(typeof(TextBlock));
        badgeText.SetValue(TextBlock.TextProperty, "Stok azaldı");
        badgeText.SetValue(TextBlock.FontSizeProperty, 11.0);
        badgeText.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        badgeText.SetValue(TextBlock.ForegroundProperty, Paint("#B91C1C"));
        badgeText.SetValue(FrameworkElement.MarginProperty, new Thickness(5, 0, 0, 0));
        badgeRow.AppendChild(badgeText);
        badge.AppendChild(badgeRow);
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Durum", Width = new DataGridLength(122), CellTemplate = new DataTemplate { VisualTree = badge }
        });
        object[] Filter()
        {
            var low = products.Values.Count(IsLow);
            lowButton.Visibility = low > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (low == 0) lowOnly = false;
            SetButtonContent(lowButton, lowOnly ? "Tüm ürünler" : $"Raf sınırı · {low} ürün", lowOnly ? "\uE8BB" : "\uE7BA");
            return products.Values
                .Where(p => !lowOnly || IsLow(p))
                .Select(p => (Product: p, Category: CategoryPath(CategoryOf(p).Id)))
                .Where(x => Matches(search.Text.Trim(), x.Product.Code, x.Product.Name, x.Product.Barcode, x.Category))
                .OrderBy(x => x.Product.Name, StringComparer.Create(culture, true))
                .Select(x => new ProductRow(Photo(CoverOf(x.Product), 96), x.Product.Code,
                    x.Product.Name + (x.Product.Variation.Length > 0 ? $"  · {VariationName(x.Product)}" : ""), x.Category,
                    HasBarcode(x.Product) ? x.Product.Barcode : "Barkodsuz", Format(ListNet(x.Product)),
                    "%" + VatOf(x.Product).ToString(culture), Format(ListGross(x.Product)),
                    x.Product.SalePrice < x.Product.Price
                        ? $"{Format(SaleGross(x.Product))}  (−%{DiscountRate(ListNet(x.Product), SaleNet(x.Product)).ToString("0.##", culture)})" : "",
                    $"{stock.GetValueOrDefault(x.Product.Code).Shop} {x.Product.Unit.ToLower(culture)}",
                    IsLow(x.Product) ? Visibility.Visible : Visibility.Collapsed)).ToArray();
        }
        views.Add((grid, Filter));
        search.TextChanged += (_, _) => grid.ItemsSource = Filter();
        search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (grid.Items.Count == 1 && grid.Items[0] is ProductRow only && products.TryGetValue(only.Code, out var match)) Run(() => EditProduct(match));
        };
        body.Children.Add(TableCard(grid, tools));

        Product Selected() => grid.SelectedItem is ProductRow row && products.TryGetValue(row.Code, out var product)
            ? product : throw new InvalidOperationException("Listeden bir ürün seç.");

        Append(actions, Act("Excel / CSV", () => ShowDataTransfer("Ürünler"), glyph: "\uE9F9"));
        Append(actions, Act("Varyasyonlar", ShowVariations, glyph: "\uE8FD"));
        Append(actions, Act("Kategoriler", ShowCategories, glyph: ""));
        Append(actions, Separator());
        Append(actions, Act("Yeni ürün", () => EditProduct(null), "SuccessButton", ""));
        Append(actions, Act("Düzenle", () => EditProduct(Selected()), "PrimaryButton", ""));
        Append(actions, Act("Sil", () =>
        {
            var product = Selected();
            ConfirmDelete(product.Name, () =>
            {
                Save("product", product with { Archived = true });
                Toast("Ürün silindi.");
            });
        }, "DangerButton", ""));
        OnOpen(grid, () => EditProduct(Selected()));
    }

    private static decimal DiscountRate(long price, long discounted) =>
        price <= 0 ? 0 : Math.Round((1 - (decimal)discounted / price) * 100, 2);

    private bool TryMoney(string text, out long cents)
    {
        cents = 0;
        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, culture, out var value) || value < 0 || value > 100_000_000m)
            return false;
        cents = (long)Math.Round(value * 100, MidpointRounding.AwayFromZero);
        return true;
    }

    private void EditProduct(Product? product)
    {
        var editing = product is not null;
        var code = Text(product?.Code ?? NextProductCode());
        code.IsReadOnly = editing;
        code.MaxLength = 40;
        if (editing) code.ToolTip = "Kayıtlı ürünün kodu değiştirilemez.";
        var noBarcode = new CheckBox { Content = "Barkodsuz", FontSize = 12, MinHeight = 0, IsChecked = product is not null && !HasBarcode(product) };
        noBarcode.ToolTip = "Barkodu olmayan ürünü barkodsuz kaydet.";
        System.Windows.Automation.AutomationProperties.SetName(noBarcode, "Barkodsuz ürün");
        var barcode = Text(product is not null && HasBarcode(product) ? product.Barcode : "");
        barcode.MaxLength = 128;
        barcode.FontFamily = new FontFamily("Consolas, Segoe UI");
        barcode.ToolTip = "EAN-13, EAN-8, UPC, Code 128, QR ve diğer barkod türleri desteklenir.";
        barcode.GotKeyboardFocus += (_, _) => barcode.SelectAll();
        barcode.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (barcode.IsKeyboardFocusWithin) return;
            e.Handled = true;
            barcode.Focus();
        };
        var clearBarcode = Act("Temizle", () => { barcode.Clear(); barcode.Focus(); }, glyph: "");
        clearBarcode.Margin = new Thickness(SmallGap, 0, 0, 0);
        var barcodeRow = new DockPanel();
        DockPanel.SetDock(clearBarcode, Dock.Right);
        barcodeRow.Children.Add(clearBarcode);
        barcodeRow.Children.Add(barcode);
        void BarcodeState()
        {
            var off = noBarcode.IsChecked == true;
            barcode.IsEnabled = clearBarcode.IsEnabled = !off;
            if (off) barcode.Clear();
        }
        noBarcode.Checked += (_, _) => BarcodeState();
        noBarcode.Unchecked += (_, _) => { BarcodeState(); barcode.Focus(); };
        BarcodeState();

        var name = Text(product?.Name ?? "");
        name.MaxLength = 200;
        barcode.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; name.Focus(); } };
        var description = Multiline(product?.Description ?? "", 72);
        description.MaxLength = 4000;
        var category = CategoryBox(product?.Category);
        var minimum = Text((product?.Minimum ?? 0).ToString(culture));
        var unit = new ComboBox { IsEditable = true, ItemsSource = Units, MaxDropDownHeight = 320, Text = product?.Unit ?? "Adet" };
        var variation = VariationEditor(product);

        string Money2(long cents) => (cents / 100m).ToString("N2", culture);
        var hasProductDiscount = product is not null && product.SalePrice < product.Price;
        var price = Text(product is null ? "" : Money2(ListNet(product)));
        var vat = VatBox(product is null ? DefaultVat : VatOf(product));
        var gross = Text(product is null ? "" : Money2(ListGross(product)));
        var discounted = Text(hasProductDiscount ? Money2(SaleNet(product!)) : "");
        var rate = Text(hasProductDiscount ? DiscountRate(ListNet(product!), SaleNet(product!)).ToString("0.##", culture) : "");
        var discountedGross = Text();
        Grid Prefixed(TextBox box, string prefix, string automation)
        {
            var host = new Grid();
            box.Padding = new Thickness(30, 0, 10, 0);
            System.Windows.Automation.AutomationProperties.SetName(box, automation);
            host.Children.Add(box);
            var mark = Label(prefix, 13, Muted, true);
            mark.Margin = new Thickness(12, 0, 0, 0);
            mark.VerticalAlignment = VerticalAlignment.Center;
            mark.IsHitTestVisible = false;
            host.Children.Add(mark);
            return host;
        }
        System.Windows.Automation.AutomationProperties.SetName(price, "KDV hariç fiyat");
        System.Windows.Automation.AutomationProperties.SetName(gross, "KDV dahil fiyat");
        System.Windows.Automation.AutomationProperties.SetName(discounted, "İndirimli fiyat (KDV hariç)");
        System.Windows.Automation.AutomationProperties.SetName(discountedGross, "İndirimli fiyat (KDV dahil)");
        var effective = Label("", 12, Muted);
        effective.Margin = new Thickness(0, SmallGap, 0, 0);
        effective.TextWrapping = TextWrapping.Wrap;

        var syncing = false;
        void Recalculate(object source)
        {
            if (syncing) return;
            syncing = true;
            try
            {
                var hasVat = int.TryParse(vat.Text.Trim().TrimStart('%'), NumberStyles.Integer, culture, out var vatRate) && vatRate is >= 0 and <= 100;
                if (!hasVat) vatRate = 0;
                if (source == gross)
                {
                    if (hasVat && TryMoney(gross.Text, out var g) && gross.Text.Trim().Length > 0) price.Text = Money2(WithoutVat(g, vatRate));
                    else if (gross.Text.Trim().Length == 0) price.Text = "";
                }
                var hasPrice = TryMoney(price.Text, out var net) && price.Text.Trim().Length > 0;
                if (source != gross) gross.Text = hasPrice && hasVat ? Money2(WithVat(net, vatRate)) : "";

                if (source == discountedGross)
                {
                    // KDV dahil indirimli fiyattan geriye: KDV hariç indirimli fiyat ve indirim oranı.
                    if (discountedGross.Text.Trim().Length == 0) discounted.Text = "";
                    else if (hasVat && TryMoney(discountedGross.Text, out var dg)) discounted.Text = Money2(WithoutVat(dg, vatRate));
                }
                if (source == discounted || source == discountedGross)
                {
                    if (discounted.Text.Trim().Length == 0) rate.Text = "";
                    else if (hasPrice && net > 0 && TryMoney(discounted.Text, out var value) && value <= net)
                        rate.Text = DiscountRate(net, value).ToString("0.##", culture);
                }
                else if (rate.Text.Trim().Length > 0)
                {
                    if (hasPrice && TryPercent(rate.Text, out var r)) discounted.Text = Money2(AfterDiscount(net, r));
                }
                else if (source == rate) discounted.Text = "";

                var hasDiscount = discounted.Text.Trim().Length > 0 && TryMoney(discounted.Text, out var final) && hasPrice && final < net;
                TryMoney(discounted.Text, out var shown);
                var saleNet = hasDiscount ? shown : net;
                if (source != discountedGross) discountedGross.Text = hasDiscount && hasVat ? Money2(WithVat(shown, vatRate)) : "";
                effective.Text = !hasPrice ? "KDV hariç fiyatı veya KDV dahil fiyatı gir; diğeri otomatik hesaplanır."
                    : !hasVat ? "KDV oranı 0 ile 100 arasında olmalı."
                    : $"{Format(saleNet)} + %{vatRate} KDV ({Format(WithVat(saleNet, vatRate) - saleNet)}) = " +
                      $"Sepete {Format(WithVat(saleNet, vatRate))} olarak yansır" +
                      (hasDiscount ? $" · {Format(net - shown)} indirim (KDV hariç)" : "");
                effective.Foreground = Paint(hasDiscount ? "#15803D" : Muted);
            }
            finally { syncing = false; }
        }
        price.TextChanged += (_, _) => Recalculate(price);
        gross.TextChanged += (_, _) => Recalculate(gross);
        vat.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => Recalculate(vat)));
        rate.TextChanged += (_, _) => Recalculate(rate);
        discounted.TextChanged += (_, _) => Recalculate(discounted);
        discountedGross.TextChanged += (_, _) => Recalculate(discountedGross);
        Recalculate(price);

        var photos = (product?.Photos ?? []).ToList();
        var cover = product is null ? "" : CoverOf(product) ?? "";
        var photoEditor = PhotoEditor(photos, () => cover, value => cover = value);

        var root = new StackPanel();
        root.Children.Add(Section("Ürün bilgileri"));
        root.Children.Add(Form(2,
            F(Field("Ürün kodu", code, true)), F(Field("Barkod", barcodeRow, aside: noBarcode)),
            F(Field("Ürün adı", name, true), 2),
            F(Field("Ürün açıklaması", description), 2),
            F(Field("Ürün kategorisi", category, true)), F(Field("Birim", unit, true)),
            F(Field("Raf alt sınırı", minimum)), F(new Border())));
        var minimumHint = Label("Dükkân stoku bu sayıya veya altına inince ürün \"Stok azaldı\" olarak işaretlenir. 0: uyarı yok.", 12, Muted);
        minimumHint.TextWrapping = TextWrapping.Wrap;
        minimumHint.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(minimumHint);
        root.Children.Add(Section("Varyasyon", PagePadding));
        root.Children.Add(variation.View);
        root.Children.Add(Section("Fiyat", PagePadding));
        root.Children.Add(Form(3,
            F(Field("KDV hariç fiyat (TL)", price, true)), F(Field("KDV oranı (%)", vat, true)),
            F(Field("KDV dahil fiyat (TL)", gross)),
            F(Field("İndirim oranı", Prefixed(rate, "%", "İndirim oranı"))), F(Field("İndirimli fiyat (KDV hariç)", discounted)),
            F(Field("İndirimli fiyat (KDV dahil)", discountedGross))));
        root.Children.Add(effective);
        var photoTitle = new DockPanel { Margin = new Thickness(0, PagePadding, 0, 0) };
        var photoHint = Label("Sürükleyip bırakabilirsin · yıldız ile kapak fotoğrafını seç", 12, Muted);
        photoHint.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(photoHint, Dock.Right);
        photoTitle.Children.Add(photoHint);
        photoTitle.Children.Add(Section("Fotoğraflar"));
        root.Children.Add(photoTitle);
        root.Children.Add(photoEditor);

        var modal = ShowModal(editing ? "Ürünü düzenle" : "Yeni ürün", root, 720, 820);
        Append(modal.Footer, Act("Vazgeç", modal.Close));
        Append(modal.Footer, Act(editing ? "Güncelle" : "Ürünü ekle", () =>
        {
            var id = code.Text.Trim().ToUpperInvariant();
            if (id.Length == 0) throw new InvalidOperationException("Ürün kodu boş olamaz.");
            if (editing && !products.ContainsKey(id)) throw new InvalidOperationException("Ürün başka bir cihazda silinmiş.");
            if (!editing && Read<Product>("product").Any(p => p.Value.Code.Equals(id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"\"{id}\" ürün kodu daha önce kullanılmış. Başka bir kod gir.");
            var bar = noBarcode.IsChecked == true ? "" : barcode.Text.Trim();
            if (bar.Length == 0 && noBarcode.IsChecked != true)
                throw new InvalidOperationException("Barkodu okut/yaz veya \"Barkodsuz\" kutusunu işaretle.");
            if (bar == DefaultBarcode) bar = "";
            if (bar.Length > 0 && products.Values.Any(p => !p.Code.Equals(id, StringComparison.OrdinalIgnoreCase) && p.Barcode == bar))
                throw new InvalidOperationException("Bu barkod başka bir üründe kullanılıyor.");
            var title = name.Text.Trim();
            if (title.Length == 0) throw new InvalidOperationException("Ürün adı boş olamaz.");
            if (category.SelectedItem is not CategoryChoice chosen) throw new InvalidOperationException("Ürün kategorisi seç.");
            if (price.Text.Trim().Length == 0 || !TryMoney(price.Text, out var basePrice))
                throw new InvalidOperationException("KDV hariç fiyatı 0,00 biçiminde gir.");
            var vatRate = ReadVat(vat);
            long? sale = null;
            if (discounted.Text.Trim().Length > 0)
            {
                if (!TryMoney(discounted.Text, out var value)) throw new InvalidOperationException("İndirimli fiyatı 0,00 biçiminde gir.");
                if (value > basePrice) throw new InvalidOperationException("İndirimli fiyat, KDV hariç fiyattan yüksek olamaz.");
                if (value < basePrice) sale = value;
            }
            else if (rate.Text.Trim().Length > 0 &&
                     !TryPercent(rate.Text, out _))
                throw new InvalidOperationException("İndirim oranı 0 ile 100 arasında olmalı.");
            var unitName = unit.Text.Trim();
            if (unitName.Length == 0) throw new InvalidOperationException("Birim seç veya yaz.");
            if (unitName.Length > 30) throw new InvalidOperationException("Birim adı en fazla 30 karakter olabilir.");
            var (variationId, options) = variation.Read();
            Save("product", new Product(id, title, bar, basePrice, Quantity(minimum, true), false,
                description.Text.Trim(), chosen.Id, sale, photos.ToArray(), cover, vatRate, true, unitName, variationId, options));
            modal.Close();
            Toast(editing ? "Ürün güncellendi." : "Ürün eklendi.");
        }, editing ? "PrimaryButton" : "SuccessButton", editing ? "" : ""));
        if (!editing) modal.Footer.Loaded += (_, _) => name.Focus();
    }

    // ───────────── Cariler ─────────────

    private sealed record AccountRow(string Code, string Name, string Type, string Phone, string Email, string Receivable, string Payable);

    private void AddAccounts()
    {
        var body = AddPage("Cariler", "Müşteri kartları, bakiyeler, tahsilat ve ödemeler", "", out var actions);
        var tools = SearchField("Cari kodu, ad, telefon veya e-posta ara", out var search);
        tools.Width = 360;
        tools.HorizontalAlignment = HorizontalAlignment.Left;
        var grid = Table();
        Columns(grid, ("Kod", "Code", 0.7), ("Cari hesap adı", "Name", 1.8), ("Tip", "Type", 0.7), ("Telefon", "Phone", 1.1),
            ("E-posta", "Email", 1.4), ("Alacak", "Receivable", 0.9), ("Borç", "Payable", 0.9));
        grid.ToolTip = "Alacak: cari bize borçlu · Borç: biz cariye borçluyuz";
        object[] Filter() => accounts.Values
            .Where(a => Matches(search.Text.Trim(), [a.Code, a.Name, a.Contact, a.TaxNumber,
                .. (a.Phones ?? []).Select(p => p.Number), .. (a.Emails ?? []).Select(e => e.Address + " " + e.Name)]))
            .OrderBy(a => a.Name, StringComparer.Create(culture, true))
            .Select(Row).ToArray();
        AccountRow Row(Account a)
        {
            var b = AccountBalance(a.Code);
            return new AccountRow(a.Code, a.Name, a.Type,
                a.Phones is { Length: > 0 } phones ? phones[0].Number + (phones.Length > 1 ? $"  +{phones.Length - 1}" : "") : "",
                a.Emails is { Length: > 0 } emails ? emails[0].Address + (emails.Length > 1 ? $"  +{emails.Length - 1}" : "") : "",
                b > 0 ? Format(b) : "", b < 0 ? Format(-b) : "");
        }
        views.Add((grid, Filter));
        search.TextChanged += (_, _) => grid.ItemsSource = Filter();
        var allMoves = View(() => accounts.Values.SelectMany(a => AccountMoves(a.Code).Select(m => (Account: a, Move: m)))
            .OrderByDescending(x => x.Move.Date)
            .Select(x => (object)new { Tarih = x.Move.Date, Cari = x.Account.Name, İşlem = x.Move.Kind, Açıklama = x.Move.Detail,
                Alacak = x.Move.Debit > 0 ? Format(x.Move.Debit) : "", Borç = x.Move.Credit > 0 ? Format(x.Move.Credit) : "" }).ToArray());
        body.Children.Add(Segments(out _, ("Cari listesi", TableCard(grid, tools)), ("Tüm cari hareketleri", Card(allMoves))));

        Account Selected() => grid.SelectedItem is AccountRow row && accounts.TryGetValue(row.Code, out var item)
            ? item : throw new InvalidOperationException("Listeden bir cari seç.");
        Account? SelectedOrNull() => grid.SelectedItem is AccountRow row && accounts.TryGetValue(row.Code, out var item) ? item : null;

        Append(actions, Act("Cari hareketleri", () => AccountStatement(Selected()), glyph: "\uE9D2"));
        Append(actions, Act("Tahsilat al", () => Movement("Tahsilat", 1, SelectedOrNull()), glyph: ""));
        Append(actions, Act("Ödeme yap", () => Movement("Cari ödeme", -1, SelectedOrNull()), glyph: ""));
        Append(actions, Separator());
        Append(actions, Act("Yeni cari", () => Edit(null), "SuccessButton", ""));
        Append(actions, Act("Düzenle", () => Edit(Selected()), "PrimaryButton", ""));
        Append(actions, Act("Sil", () =>
        {
            var account = Selected();
            if (AccountBalance(account.Code) != 0)
                throw new InvalidOperationException("Cariyi silmeden önce bakiyesini kapat.");
            ConfirmDelete(account.Name, () =>
            {
                Save("account", account with { Archived = true });
                Toast("Cari silindi.");
            });
        }, "DangerButton", ""));
        OnOpen(grid, () => Edit(Selected()));

        void Edit(Account? account)
        {
            var editing = account is not null;
            var form = AccountEditor(account, !editing);
            var modal = ShowModal(editing ? "Cariyi düzenle" : "Yeni cari", form.View, 680, 820);
            Append(modal.Footer, Act("Vazgeç", modal.Close));
            Append(modal.Footer, Act(editing ? "Güncelle" : "Cariyi ekle", () =>
            {
                var value = form.Build();
                if (editing && !accounts.ContainsKey(value.Code)) throw new InvalidOperationException("Cari başka bir cihazda silinmiş.");
                if (!editing) EnsureNewAccountCode(value.Code);
                Save("account", value);
                modal.Close();
                Toast(editing ? "Cari güncellendi." : "Cari eklendi.");
            }, editing ? "PrimaryButton" : "SuccessButton", editing ? "" : ""));
        }

        void Movement(string kind, int sign, Account? account)
        {
            if (accounts.Count == 0) throw new InvalidOperationException("Önce bir cari ekle.");
            var picker = AccountBox(account?.Code);
            var amount = Text();
            var detail = Text(kind);
            string Describe(string code) => AccountBalance(code) is var value && value > 0 ? $"Alacağımız: {Format(value)}"
                : value < 0 ? $"Borcumuz: {Format(-value)}" : "Bakiye: 0";
            var balance = Label(account is null ? "" : Describe(account.Code), 12, Muted);
            picker.SelectionChanged += (_, _) => balance.Text = picker.SelectedItem is AccountChoice c ? Describe(c.Code) : "";
            var cari = new StackPanel();
            cari.Children.Add(Field("Cari", picker));
            balance.Margin = new Thickness(0, 6, 0, 0);
            cari.Children.Add(balance);
            var form = Form(2, F(cari, 2), F(Field("Tutar (TL)", amount)), F(Field("Açıklama", detail)));
            var modal = ShowModal(sign > 0 ? "Tahsilat al" : "Cariye ödeme yap", form, 520,
                subtitle: sign > 0 ? "Cariden alınan ödeme alacağımızı azaltır." : "Cariye (ör. tedarikçiye) yapılan ödeme borcumuzu azaltır.");
            Append(modal.Footer, Act("Vazgeç", modal.Close));
            Append(modal.Footer, Act("Kaydet", () =>
            {
                var target = ResolveAccount(picker);
                Save("cash", new Cash(kind, Required(detail), target.Code, sign * Money(amount)));
                modal.Close();
                Toast($"{kind} kaydedildi · {target.Name}");
            }, "SuccessButton", ""));
        }
    }

    private void EnsureNewAccountCode(string code)
    {
        if (code.Length == 0) throw new InvalidOperationException("Cari kodu boş olamaz.");
        if (Read<Account>("account").Any(a => a.Value.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"\"{code}\" cari kodu daha önce kullanılmış. Başka bir kod gir.");
    }

    private sealed record AccountForm(FrameworkElement View, Func<Account> Build, TextBox Code);

    /// <summary>Cari kartı formu. Yeni cari, düzenleme ve teklifteki manuel müşteri aynı formu kullanır.</summary>
    private AccountForm AccountEditor(Account? account, bool codeEditable)
    {
        // Eski kayıtlarda adres ve iletişim tek alandaydı; düzenlerken adrese taşınır.
        var legacy = account is { Phones: null, Emails: null, Address: "", BillingAddress: "" } && account.Contact.Length > 0;

        var code = Text(account?.Code ?? NextAccountCode());
        code.MaxLength = 40;
        code.IsReadOnly = !codeEditable;
        if (!codeEditable) code.ToolTip = "Kayıtlı carinin kodu değiştirilemez.";
        var type = new ComboBox { ItemsSource = new[] { "Kurumsal", "Bireysel", "Tedarikçi" } };
        type.SelectedItem = account?.Type is "Bireysel" or "Tedarikçi" ? account.Type : "Kurumsal";
        var name = Text(account?.Name ?? "");
        name.MaxLength = 200;

        // Telefonlar
        var phones = (account?.Phones ?? []).ToList();
        var phoneKind = new ComboBox { ItemsSource = new[] { "Cep", "Sabit" }, SelectedIndex = 0, Width = 96 };
        System.Windows.Automation.AutomationProperties.SetName(phoneKind, "Telefon türü");
        var phoneNumber = Text();
        phoneNumber.MaxLength = 30;
        System.Windows.Automation.AutomationProperties.SetName(phoneNumber, "Telefon numarası");
        var phoneList = new StackPanel();
        void AddPhone()
        {
            var number = phoneNumber.Text.Trim();
            if (number.Length == 0) throw new InvalidOperationException("Telefon numarasını yaz.");
            if (!number.Any(char.IsDigit)) throw new InvalidOperationException("Geçerli bir telefon numarası yaz.");
            phones.Add(new Phone((string)phoneKind.SelectedItem, number));
            phoneNumber.Clear();
            phoneNumber.Focus();
            RenderPhones();
        }
        void RenderPhones()
        {
            phoneList.Children.Clear();
            foreach (var phone in phones.ToArray())
                phoneList.Children.Add(Chip(phone.Kind, phone.Kind == "Cep" ? "" : "", phone.Number,
                    () => { phones.Remove(phone); RenderPhones(); }));
            phoneList.Margin = new Thickness(0, phones.Count > 0 ? SmallGap : 0, 0, 0);
        }
        phoneNumber.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Run(AddPhone); } };
        var phoneInput = InputRow(AddPhone, "Telefon ekle", (phoneKind, GridLength.Auto), (Hinted(phoneNumber, "0 5xx xxx xx xx"), new GridLength(1, GridUnitType.Star)));
        var phoneField = new StackPanel();
        phoneField.Children.Add(phoneInput);
        phoneField.Children.Add(phoneList);
        RenderPhones();

        // E-postalar
        var emails = (account?.Emails ?? []).ToList();
        var emailName = Text();
        emailName.MaxLength = 120;
        System.Windows.Automation.AutomationProperties.SetName(emailName, "E-posta sahibi veya birim");
        var emailAddress = Text();
        emailAddress.MaxLength = 200;
        System.Windows.Automation.AutomationProperties.SetName(emailAddress, "E-posta adresi");
        var emailList = new StackPanel();
        void AddEmail()
        {
            var address = emailAddress.Text.Trim();
            if (address.Length == 0) throw new InvalidOperationException("E-posta adresini yaz.");
            if (!IsEmail(address)) throw new InvalidOperationException("Geçerli bir e-posta adresi yaz (ornek@firma.com).");
            emails.Add(new Email(emailName.Text.Trim(), address));
            emailName.Clear();
            emailAddress.Clear();
            emailName.Focus();
            RenderEmails();
        }
        void RenderEmails()
        {
            emailList.Children.Clear();
            foreach (var email in emails.ToArray())
                emailList.Children.Add(Chip(email.Name.Length > 0 ? email.Name : "E-posta", "", email.Address,
                    () => { emails.Remove(email); RenderEmails(); }));
            emailList.Margin = new Thickness(0, emails.Count > 0 ? SmallGap : 0, 0, 0);
        }
        emailAddress.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Run(AddEmail); } };
        var emailInput = InputRow(AddEmail, "E-posta ekle", (Hinted(emailName, "İsim veya birim (ör. Muhasebe)"), new GridLength(2, GridUnitType.Star)),
            (Hinted(emailAddress, "ornek@firma.com"), new GridLength(3, GridUnitType.Star)));
        var emailField = new StackPanel();
        emailField.Children.Add(emailInput);
        emailField.Children.Add(emailList);
        RenderEmails();

        var taxNumber = Text(account?.TaxNumber ?? "");
        taxNumber.MaxLength = 20;
        var taxOffice = Text(account?.TaxOffice ?? "");
        taxOffice.MaxLength = 100;
        var billing = Multiline(account?.BillingAddress ?? "", 64);
        billing.MaxLength = 1000;
        var address = Multiline(legacy ? account!.Contact : account?.Address ?? "", 64);
        address.MaxLength = 1000;
        var same = new CheckBox { Content = "Fatura adresi ile aynı", FontSize = 12, MinHeight = 0, IsChecked = account?.SameAddress == true };
        void Mirror()
        {
            if (same.IsChecked != true) { address.IsEnabled = true; return; }
            address.Text = billing.Text;
            address.IsEnabled = false;
        }
        same.Checked += (_, _) => Mirror();
        same.Unchecked += (_, _) => { address.IsEnabled = true; address.Focus(); };
        billing.TextChanged += (_, _) => { if (same.IsChecked == true) address.Text = billing.Text; };
        Mirror();
        var website = Text(account?.Website ?? "");
        website.MaxLength = 200;

        var root = new StackPanel();
        root.Children.Add(Section("Cari bilgileri"));
        root.Children.Add(Form(2, F(Field("Cari kodu", code, true)), F(Field("Cari tipi", type, true)),
            F(Field("Cari hesap adı", name, true), 2)));
        root.Children.Add(Section("İletişim", PagePadding));
        root.Children.Add(Form(1, F(Field("Telefon numaraları", phoneField)), F(Field("E-posta adresleri", emailField))));
        root.Children.Add(Section("Vergi ve adres", PagePadding));
        root.Children.Add(Form(2, F(Field("Vergi numarası", taxNumber)), F(Field("Vergi dairesi", taxOffice)),
            F(Field("Fatura adresi", billing), 2), F(Field("Adres", address, aside: same), 2), F(Field("Web sitesi", Hinted(website, "www.firma.com")), 2)));

        Account Build()
        {
            if (phoneNumber.Text.Trim().Length > 0) AddPhone();
            if (emailAddress.Text.Trim().Length > 0) AddEmail();
            var id = code.Text.Trim().ToUpperInvariant();
            if (id.Length == 0) throw new InvalidOperationException("Cari kodu boş olamaz.");
            var title = name.Text.Trim();
            if (title.Length == 0) throw new InvalidOperationException("Cari hesap adı boş olamaz.");
            var value = new Account(id, title, "", false, (string)type.SelectedItem, phones.ToArray(), emails.ToArray(),
                taxNumber.Text.Trim(), taxOffice.Text.Trim(), billing.Text.Trim(),
                same.IsChecked == true ? billing.Text.Trim() : address.Text.Trim(), same.IsChecked == true, website.Text.Trim());
            return value with { Contact = ContactSummary(value) };
        }
        return new AccountForm(root, Build, code);
    }

    private static bool IsEmail(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    /// <summary>Teklif ve çıktılarda kullanılan tek metin iletişim özeti.</summary>
    private static string ContactSummary(Account account)
    {
        var lines = new List<string>();
        if (account.TaxOffice.Length > 0 || account.TaxNumber.Length > 0)
            lines.Add(string.Join(" · ", new[] { account.TaxOffice.Length > 0 ? account.TaxOffice + " V.D." : "",
                account.TaxNumber.Length > 0 ? "VKN/TCKN " + account.TaxNumber : "" }.Where(s => s.Length > 0)));
        var address = account.BillingAddress.Length > 0 ? account.BillingAddress : account.Address;
        if (address.Length > 0) lines.Add(address.ReplaceLineEndings(" "));
        if (account.Phones is { Length: > 0 } phones) lines.Add(string.Join(" · ", phones.Select(p => $"{p.Kind}: {p.Number}")));
        if (account.Emails is { Length: > 0 } emails)
            lines.Add(string.Join(" · ", emails.Select(e => e.Name.Length > 0 ? $"{e.Name}: {e.Address}" : e.Address)));
        if (account.Website.Length > 0) lines.Add(account.Website);
        return string.Join("\n", lines);
    }

    /// <summary>Girişler + sağda "Ekle" butonu.</summary>
    private Grid InputRow(Action add, string label, params (FrameworkElement Input, GridLength Width)[] inputs)
    {
        var row = new Grid();
        for (var i = 0; i < inputs.Length; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = inputs[i].Width });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SmallGap) });
            Grid.SetColumn(inputs[i].Input, i * 2);
            row.Children.Add(inputs[i].Input);
        }
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var button = Act("Ekle", add, "SuccessButton", "");
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        Grid.SetColumn(button, inputs.Length * 2);
        row.Children.Add(button);
        return row;
    }

    /// <summary>Kutuyu, boşken soluk ipucu metni gösteren bir kapsayıcıya sarar.</summary>
    private static Grid Hinted(TextBox box, string text)
    {
        var host = new Grid();
        host.Children.Add(box);
        var hint = Label(text, 13, "#9CA3AF");
        hint.Margin = new Thickness(11, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.IsHitTestVisible = false;
        hint.TextTrimming = TextTrimming.CharacterEllipsis;
        host.Children.Add(hint);
        void Update() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Update();
        Update();
        return host;
    }

    /// <summary>Eklenmiş telefon / e-posta satırı.</summary>
    private Border Chip(string kind, string glyph, string value, Action remove)
    {
        var row = new DockPanel();
        var delete = Act("", remove, "GhostButton", "");
        delete.Width = 28;
        delete.Height = 28;
        delete.MinHeight = 28;
        delete.Padding = new Thickness(0);
        delete.Foreground = Paint("#DC2626");
        delete.ToolTip = "Kaldır";
        System.Windows.Automation.AutomationProperties.SetName(delete, $"{value} kaldır");
        DockPanel.SetDock(delete, Dock.Right);
        row.Children.Add(delete);
        var icon = Glyph(glyph, 13, "#2563EB");
        icon.Width = 24;
        row.Children.Add(icon);
        var tag = new Border
        {
            Background = Paint("#EFF4FF"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 3),
            Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = Label(kind, 11, "#1D4ED8", true)
        };
        row.Children.Add(tag);
        var text = Label(value, 13);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Children.Add(text);
        return new Border
        {
            Child = row, Padding = new Thickness(10, 4, 4, 4), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(8),
            Background = Paint("#F9FAFB"), BorderBrush = Paint(LineColor), BorderThickness = new Thickness(1)
        };
    }

    // ───────────── Teklifler ─────────────

    private Dictionary<Guid, (DateTime Date, Quote Quote)> Quotes()
    {
        var quotes = Read<Quote>("quote").ToDictionary(q => q.Entry.Id, q => (q.Entry.CreatedAt.LocalDateTime, q.Value));
        foreach (var change in Read<QuoteRevision>("quote-revision"))
        {
            var id = change.Value.QuoteId;
            if (change.Value.Value is null) quotes.Remove(id);
            else if (quotes.TryGetValue(id, out var original)) quotes[id] = (original.LocalDateTime, change.Value.Value);
        }
        return quotes;
    }

    private sealed record QuoteRow(Guid No, string Customer, DateTime Date, int Lines, long Total);

    private void AddQuotes()
    {
        var body = AddPage("Teklifler", "Müşterilere fiyat teklifi hazırla, yazdır veya satışa aktar", "", out var actions);
        var tools = SearchField("Müşteri ara", out var search);
        tools.Width = 360;
        tools.HorizontalAlignment = HorizontalAlignment.Left;
        var grid = Table();
        grid.AutoGenerateColumns = false;
        grid.Columns.Add(new DataGridTextColumn { Header = "Müşteri", Binding = new System.Windows.Data.Binding("Customer"), Width = new DataGridLength(2.2, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Tarih", Binding = new System.Windows.Data.Binding("Date") { StringFormat = "dd.MM.yyyy HH:mm" }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Kalem", Binding = new System.Windows.Data.Binding("Lines"), Width = new DataGridLength(0.6, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Toplam", Binding = new System.Windows.Data.Binding("Total") { Converter = new MoneyConverter(culture) }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        object[] Filter() => Quotes().Where(q => Matches(search.Text.Trim(), q.Value.Quote.Customer))
            .OrderByDescending(q => q.Value.Date).Select(q => new QuoteRow(
                q.Key, q.Value.Quote.Customer, q.Value.Date, q.Value.Quote.Lines.Length, Sum(q.Value.Quote.Lines))).ToArray();
        views.Add((grid, Filter));
        search.TextChanged += (_, _) => grid.ItemsSource = Filter();
        body.Children.Add(TableCard(grid, tools));

        Guid Selected() => grid.SelectedItem is QuoteRow selected ? selected.No
            : throw new InvalidOperationException("Listeden bir teklif seç.");

        Append(actions, Act("Excel / CSV", () => ShowDataTransfer("Teklifler"), glyph: ""));
        Append(actions, Act("Satışa aktar", ToBasket, glyph: ""));
        Append(actions, Act("PDF", ExportPdf, glyph: ""));
        Append(actions, Act("Yazdır", PrintQuote, glyph: ""));
        Append(actions, Separator());
        Append(actions, Act("Yeni teklif", () => Edit(null), "SuccessButton", ""));
        Append(actions, Act("Düzenle", () => Edit(Selected()), "PrimaryButton", ""));
        Append(actions, Act("Sil", () =>
        {
            var id = Selected();
            ConfirmDelete(Quotes()[id].Quote.Customer + " teklifi", () =>
            {
                Save("quote-revision", new QuoteRevision(id, null));
                Toast("Teklif silindi.");
            });
        }, "DangerButton", ""));
        OnOpen(grid, () => Edit(Selected()));

        void Edit(Guid? id)
        {
            var quote = id is Guid value ? Quotes()[value].Quote : null;
            var accountCode = quote?.AccountCode ?? "";
            var dirty = false;
            quoteLines.Clear();
            foreach (var line in quote?.Lines ?? []) quoteLines.Add(line);

            var root = new StackPanel();
            var registered = accountCode.Length > 0 && accounts.ContainsKey(accountCode);
            var picker = AccountBox(registered ? accountCode : null);
            var summary = new StackPanel();
            void ShowSummary()
            {
                summary.Children.Clear();
                if (TryAccount(picker) is not { } account)
                {
                    var hint = Label(accounts.Count == 0 ? "Henüz kayıtlı cari yok. Manuel müşteri sekmesinden devam edebilirsin."
                        : "Listeden bir cari seç veya adını yazarak ara.", 12, Muted);
                    hint.Margin = new Thickness(0, SmallGap, 0, 0);
                    hint.TextWrapping = TextWrapping.Wrap;
                    summary.Children.Add(hint);
                    return;
                }
                var info = new StackPanel();
                var head = Hstack(Label(account.Name, 15, bold: true), new Border
                {
                    Background = Paint("#EFF4FF"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 3),
                    VerticalAlignment = VerticalAlignment.Center, Child = Label($"{account.Type} · {account.Code}", 11, "#1D4ED8", true)
                });
                info.Children.Add(head);
                var details = account.Phones is null && account.Emails is null ? account.Contact : ContactSummary(account);
                var text = Label(details.Length > 0 ? details : "İletişim bilgisi girilmemiş.", 13, Muted);
                text.TextWrapping = TextWrapping.Wrap;
                text.Margin = new Thickness(0, 6, 0, 0);
                info.Children.Add(text);
                var card = Card(info, new Thickness(Gap, 12, Gap, 12));
                card.Background = Paint("#F9FAFB");
                card.Margin = new Thickness(0, 12, 0, 0);
                summary.Children.Add(card);
            }
            picker.SelectionChanged += (_, _) => ShowSummary();
            picker.LostKeyboardFocus += (_, _) => ShowSummary();
            ShowSummary();
            var registeredView = new StackPanel();
            registeredView.Children.Add(Field("Kayıtlı cari", picker, true));
            registeredView.Children.Add(summary);

            var manualAccount = quote is null || registered ? null : quote.Details ?? new Account("", quote.Customer, quote.Contact);
            if (manualAccount is { Code: "" }) manualAccount = manualAccount with { Code = NextAccountCode() };
            var manualForm = AccountEditor(manualAccount, true);
            root.Children.Add(Section("Müşteri"));
            root.Children.Add(Segments(out var selectCustomer, ("Kayıtlı müşteri", registeredView), ("Manuel müşteri", manualForm.View)));
            if (quote is not null && !registered) selectCustomer(1);
            bool IsManual() => manualForm.View.Visibility == Visibility.Visible;
            root.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => dirty = true));
            root.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => dirty = true));

            root.Children.Add(Section("Kalemler", PagePadding));
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
            var add = Act("Ekle", AddLine, "SuccessButton", "");
            add.VerticalAlignment = VerticalAlignment.Bottom;
            add.Margin = new Thickness(SmallGap, 0, 0, 0);
            Grid.SetColumn(add, 2);
            addRow.Children.Add(add);
            root.Children.Add(addRow);
            productBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Run(AddLine); } };

            // Kalem tablosu: adet ve iskonto satır üzerinde düzenlenir, çöp kutusu satırı siler.
            var widths = new[] { new GridLength(1, GridUnitType.Star), new GridLength(132), new GridLength(104), new GridLength(84),
                new GridLength(52), new GridLength(112), new GridLength(44) };
            Grid LineGrid()
            {
                var grid = new Grid();
                foreach (var width in widths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
                return grid;
            }
            void Cell(Grid grid, int column, UIElement element, HorizontalAlignment align = HorizontalAlignment.Right)
            {
                if (element is FrameworkElement framework)
                {
                    framework.HorizontalAlignment = align;
                    framework.VerticalAlignment = VerticalAlignment.Center;
                }
                Grid.SetColumn(element, column);
                grid.Children.Add(element);
            }
            var header = LineGrid();
            string[] titles = ["Ürün", "Adet", "Birim (KDV hariç)", "İskonto %", "KDV", "Tutar (KDV dahil)", ""];
            for (var c = 0; c < titles.Length; c++)
            {
                var title = Label(titles[c], 12, Muted, true);
                title.TextTrimming = TextTrimming.CharacterEllipsis;
                Cell(header, c, title, c == 0 ? HorizontalAlignment.Left : c == 1 ? HorizontalAlignment.Center : HorizontalAlignment.Right);
            }
            var lineRows = new StackPanel();
            var table = new StackPanel();
            table.Children.Add(new Border
            {
                Child = header, Padding = new Thickness(Gap, 10, 12, 10), Background = Paint("#F9FAFB"),
                BorderBrush = Paint(LineColor), BorderThickness = new Thickness(0, 0, 0, 1)
            });
            table.Children.Add(lineRows);
            var linesCard = Card(table);
            linesCard.Margin = new Thickness(0, Gap, 0, 0);
            root.Children.Add(linesCard);

            // Alt bölüm: solda toplu iskonto, sağda tutar özeti.
            var bottom = new Grid { Margin = new Thickness(0, Gap, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            var bulk = Text();
            bulk.Width = 96;
            System.Windows.Automation.AutomationProperties.SetName(bulk, "Tüm kalemlere iskonto");
            var bulkApply = Act("Uygula", () =>
            {
                if (quoteLines.Count == 0) throw new InvalidOperationException("Önce kalem ekle.");
                if (!TryPercent(bulk.Text, out var percent)) throw new InvalidOperationException("İskonto 0 ile 100 arasında olmalı.");
                for (var k = 0; k < quoteLines.Count; k++) quoteLines[k] = Reprice(quoteLines[k], percent);
                Toast($"Tüm kalemlere %{percent.ToString("0.##", culture)} iskonto uygulandı.");
            }, glyph: "");
            var bulkPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            bulkPanel.Children.Add(Field("Tüm kalemlere iskonto (%)", Hstack(Hinted(bulk, "Örn. 5"), bulkApply)));
            var bulkHint = Label("Yüksek adetli kalemlere satırdaki İskonto % alanından ayrı ayrı da iskonto girebilirsin. " +
                "İskonto KDV hariç fiyata uygulanır, KDV iskontolu tutar üzerinden hesaplanır.", 12, Muted);
            bulkHint.TextWrapping = TextWrapping.Wrap;
            bulkHint.Margin = new Thickness(0, SmallGap, 0, 0);
            bulkPanel.Children.Add(bulkHint);
            bottom.Children.Add(bulkPanel);
            var totalsPanel = new StackPanel();
            totalsPanel.Children.Add(SummaryRow("Ara Toplam", out var subtotalValue));
            totalsPanel.Children.Add(SummaryRow("İskonto", out var discountValue, color: "#DC2626"));
            totalsPanel.Children.Add(SummaryRow("KDV", out var vatValue));
            totalsPanel.Children.Add(new Border { Height = 1, Background = Paint(LineColor), Margin = new Thickness(0, SmallGap, 0, 0) });
            totalsPanel.Children.Add(SummaryRow("Genel Toplam", out var totalValue, 18, true, "#1D4ED8"));
            var summaryCard = Card(totalsPanel, new Thickness(Gap, 12, Gap, 14));
            summaryCard.Background = Paint("#F9FAFB");
            Grid.SetColumn(summaryCard, 2);
            bottom.Children.Add(summaryCard);
            root.Children.Add(bottom);

            var quiet = false;
            void RenderTotals()
            {
                var totals = Summarize(quoteLines);
                subtotalValue.Text = Format(totals.Subtotal);
                discountValue.Text = totals.Discount > 0 ? "−" + Format(totals.Discount) : Format(0);
                vatValue.Text = Format(totals.Vat);
                totalValue.Text = Format(totals.Total);
            }

            void RenderLines()
            {
                lineRows.Children.Clear();
                if (quoteLines.Count == 0)
                {
                    var empty = Label("Henüz kalem eklenmedi. Yukarıdan ürün seçip Ekle butonuna bas.", 13, Muted);
                    empty.Margin = new Thickness(Gap, 20, Gap, 20);
                    lineRows.Children.Add(empty);
                }
                for (var k = 0; k < quoteLines.Count; k++) lineRows.Children.Add(LineRow(k, quoteLines[k], k == quoteLines.Count - 1));
                RenderTotals();
            }

            UIElement LineRow(int index, Line line, bool last)
            {
                var grid = LineGrid();
                var names = new StackPanel { Margin = new Thickness(0, 0, SmallGap, 0) };
                var title = Label(line.Name, 13, bold: true);
                title.TextTrimming = TextTrimming.CharacterEllipsis;
                title.ToolTip = line.Name;
                names.Children.Add(title);
                names.Children.Add(Label(line.Code, 11, Muted));
                Cell(grid, 0, names, HorizontalAlignment.Stretch);

                var amount = new StackPanel();
                void Replace(Line updated) { if (index < quoteLines.Count) quoteLines[index] = updated; }
                // Yazarken satırı yeniden çizmeden günceller (odak kutuda kalır).
                void Quiet(Line updated)
                {
                    if (index >= quoteLines.Count || updated == line) return;
                    quiet = true;
                    try { quoteLines[index] = updated; }
                    finally { quiet = false; }
                    line = updated;
                    ShowAmount();
                }
                var stepper = new StackPanel { Orientation = Orientation.Horizontal };
                Button Step(string glyph, int delta, string tip)
                {
                    var button = Act("", () =>
                    {
                        var next = line.Quantity + delta;
                        if (next < 1) throw new InvalidOperationException("Adet en az 1 olmalı. Kalemi silmek için çöp kutusunu kullan.");
                        if (next > 1_000_000) throw new InvalidOperationException("Adet sınırı aşıldı.");
                        Replace(line with { Quantity = next });
                    }, glyph: glyph);
                    button.Width = button.Height = button.MinHeight = 30;
                    button.Padding = new Thickness(0);
                    button.ToolTip = tip;
                    System.Windows.Automation.AutomationProperties.SetName(button, $"{line.Name} {tip}");
                    return button;
                }
                stepper.Children.Add(Step("", -1, "adet azalt"));
                var quantity = new TextBox
                {
                    Text = line.Quantity.ToString(culture), Width = 56, MinHeight = 30, Margin = new Thickness(4, 0, 4, 0),
                    HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(4, 0, 4, 0)
                };
                System.Windows.Automation.AutomationProperties.SetName(quantity, $"{line.Name} adet");
                stepper.Children.Add(quantity);
                stepper.Children.Add(Step("", 1, "adet artır"));
                Cell(grid, 1, stepper, HorizontalAlignment.Center);
                bool ValidQuantity(out int value) => int.TryParse(quantity.Text.Trim(), out value) && value is >= 1 and <= 1_000_000;
                quantity.TextChanged += (_, _) => { if (ValidQuantity(out var value)) Quiet(line with { Quantity = value }); };
                void CheckQuantity()
                {
                    if (ValidQuantity(out _)) return;
                    quantity.Text = line.Quantity.ToString(culture);
                    Toast("Adet 1 ile 1.000.000 arasında olmalı.", true);
                }
                quantity.LostKeyboardFocus += (_, _) => CheckQuantity();
                quantity.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; CheckQuantity(); } };

                Cell(grid, 2, Label(Format(line.Net ?? line.UnitPrice), 13));
                var discount = new TextBox
                {
                    Text = line.Discount == 0 ? "" : line.Discount.ToString("0.##", culture), Width = 68, MinHeight = 30,
                    HorizontalContentAlignment = HorizontalAlignment.Right, Padding = new Thickness(6, 0, 8, 0)
                };
                System.Windows.Automation.AutomationProperties.SetName(discount, $"{line.Name} iskonto");
                Cell(grid, 3, Hinted(discount, "0"));
                bool ValidDiscount(out decimal value)
                {
                    value = 0;
                    return discount.Text.Trim().Length == 0 || TryPercent(discount.Text, out value);
                }
                discount.TextChanged += (_, _) => { if (ValidDiscount(out var value) && value != line.Discount) Quiet(Reprice(line, value)); };
                void CheckDiscount()
                {
                    if (ValidDiscount(out _)) return;
                    discount.Text = line.Discount == 0 ? "" : line.Discount.ToString("0.##", culture);
                    Toast("İskonto 0 ile 100 arasında olmalı.", true);
                }
                discount.LostKeyboardFocus += (_, _) => CheckDiscount();
                discount.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; CheckDiscount(); } };

                Cell(grid, 4, Label("%" + (line.Vat ?? 0).ToString(culture), 13, Muted));
                void ShowAmount()
                {
                    amount.Children.Clear();
                    var total = Label(Format(line.UnitPrice * line.Quantity), 14, bold: true);
                    total.HorizontalAlignment = HorizontalAlignment.Right;
                    amount.Children.Add(total);
                    if (line.Discount <= 0) return;
                    var saved = Label($"−%{line.Discount.ToString("0.##", culture)} iskonto", 11, "#DC2626");
                    saved.HorizontalAlignment = HorizontalAlignment.Right;
                    amount.Children.Add(saved);
                }
                ShowAmount();
                Cell(grid, 5, amount);
                var delete = Act("", () => quoteLines.RemoveAt(index), "GhostButton", "");
                delete.Width = delete.Height = delete.MinHeight = 32;
                delete.Padding = new Thickness(0);
                delete.Foreground = Paint("#DC2626");
                delete.ToolTip = "Kalemi sil";
                System.Windows.Automation.AutomationProperties.SetName(delete, $"{line.Name} sil");
                Cell(grid, 6, delete);
                return new Border
                {
                    Child = grid, Padding = new Thickness(Gap, SmallGap, 12, SmallGap),
                    BorderBrush = Paint("#F0F1F4"), BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1)
                };
            }

            System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) =>
            {
                dirty = true;
                if (quiet) RenderTotals(); else RenderLines();
            };
            quoteLines.CollectionChanged += changed;
            RenderLines();

            void AddLine()
            {
                var product = ResolveProduct(productBox);
                quoteLines.Add(LineFor(product, Quantity(count)));
                productBox.SelectedItem = null;
                productBox.Text = "";
                count.Text = "1";
                productBox.Focus();
            }

            var modal = ShowModal(id is null ? "Yeni teklif" : "Teklifi düzenle", root, 820, 880);
            dirty = false;
            modal.Closed = () => { quoteLines.CollectionChanged -= changed; quoteLines.Clear(); };
            modal.RequestClose = () =>
            {
                if (!dirty) { modal.Close(); return; }
                Confirm("Kaydedilmemiş değişiklikler", "Teklifteki değişiklikler kaydedilmeden kapatılsın mı?", "Kaydetmeden kapat", modal.Close);
            };
            Append(modal.Footer, Act("Vazgeç", () => modal.RequestClose()));
            Append(modal.Footer, Act("Teklifi kaydet", () =>
            {
                if (quoteLines.Count == 0) throw new InvalidOperationException("En az bir ürün ekle.");
                if (!IsManual())
                {
                    var account = TryAccount(picker) ??
                        throw new InvalidOperationException("Kayıtlı bir cari seç veya Manuel müşteri sekmesinden devam et.");
                    var contact = account.Phones is null && account.Emails is null ? account.Contact : ContactSummary(account);
                    StoreQuote(new Quote(account.Name, contact, quoteLines.ToArray(), account.Code));
                    return;
                }
                var details = manualForm.Build();
                var message = Label($"“{details.Name}” için manuel müşteri bilgisi girdin. Bu müşteri için cari kaydı da oluşturulsun mu?", 14, "#374151");
                message.TextWrapping = TextWrapping.Wrap;
                var ask = ShowModal("Cari kaydı eklensin mi?", message, 460);
                Append(ask.Footer, Act("Hayır, sadece teklif", () =>
                {
                    ask.Close();
                    StoreQuote(new Quote(details.Name, details.Contact, quoteLines.ToArray(), "", details));
                }));
                var yes = Append(ask.Footer, Act("Evet, cari oluştur", () =>
                {
                    EnsureNewAccountCode(details.Code);
                    ask.Close();
                    Save("account", details);
                    StoreQuote(new Quote(details.Name, details.Contact, quoteLines.ToArray(), details.Code));
                    Toast($"Cari kaydı oluşturuldu · {details.Code}");
                }, "SuccessButton", ""));
                ask.Footer.Loaded += (_, _) => yes.Focus();
            }, "SuccessButton", ""));

            void StoreQuote(Quote saved)
            {
                if (id is Guid existing)
                {
                    if (!Quotes().ContainsKey(existing)) throw new InvalidOperationException("Teklif başka bir cihazda silinmiş.");
                    Save("quote-revision", new QuoteRevision(existing, saved));
                }
                else Save("quote", saved);
                modal.Close();
                Toast("Teklif kaydedildi.");
            }

        }

        void ExportPdf()
        {
            var id = Selected();
            var (date, quote) = Quotes()[id];
            var dialog = new SaveFileDialog
            {
                Title = "Teklifi PDF olarak kaydet", Filter = "PDF dosyası (*.pdf)|*.pdf",
                DefaultExt = ".pdf", AddExtension = true, OverwritePrompt = true,
                FileName = $"Aden-Teklif-{id.ToString("N")[..12]}"
            };
            if (dialog.ShowDialog(this) != true) return;
            QuotePdf.Save(dialog.FileName, id, date, quote, store.Setting("company", "Aden"),
                store.Setting("companyContact"), store.Setting("quoteLogo"), QuoteTotals(quote));
            Toast($"PDF kaydedildi · {System.IO.Path.GetFileName(dialog.FileName)}");
        }

        void PrintQuote()
        {
            var (date, quote) = Quotes()[Selected()];
            var document = new FlowDocument { PagePadding = new Thickness(45), FontFamily = FontFamily };
            var logo = Brand.Wordmark;
            var configuredLogo = store.Setting("quoteLogo");
            if (configuredLogo.Length > 0)
            {
                using var stream = new System.IO.MemoryStream(Convert.FromBase64String(configuredLogo));
                var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                logo = bitmap.Frames[0];
            }
            document.Blocks.Add(new BlockUIContainer(new Image { Source = logo, Width = 145, MaxHeight = 75,
                HorizontalAlignment = HorizontalAlignment.Left }));
            document.Blocks.Add(new Paragraph(new Bold(new Run("SATIŞ TEKLİFİ"))) { FontSize = 25 });
            document.Blocks.Add(new Paragraph(new Run($"{store.Setting("company", "Aden")}\n{store.Setting("companyContact")}\n{date:d}")));
            document.Blocks.Add(new Paragraph(new Run($"{quote.Customer}\n{quote.Contact}")));
            foreach (var line in quote.Lines)
                document.Blocks.Add(new Paragraph(new Run($"{line.Name} × {line.Quantity}" +
                    (line.Discount > 0 ? $"  (iskonto %{line.Discount.ToString("0.##", culture)})" : "") +
                    $"   KDV %{line.Vat ?? 0}   {Format(line.UnitPrice * line.Quantity)}")));
            var sums = Summarize(quote.Lines);
            document.Blocks.Add(new Paragraph(new Run($"Ara toplam {Format(sums.Subtotal)}\nİskonto −{Format(sums.Discount)}\nKDV {Format(sums.Vat)}")));
            document.Blocks.Add(new Paragraph(new Bold(new Run($"Genel toplam {Format(sums.Total)}"))));
            Print(document, "Aden satış teklifi");
        }

        void ToBasket()
        {
            var id = Selected();
            if (basket.Count > 0 || payments.Count > 0)
                throw new InvalidOperationException("Önce mevcut satış sepetini tamamla veya boşalt.");
            var quote = Quotes()[id].Quote;
            foreach (var line in quote.Lines)
                if (!products.ContainsKey(line.Code))
                    throw new InvalidOperationException($"Teklifte silinmiş ürün var: {line.Name}");
            foreach (var line in quote.Lines) basket.Add(line);
            if (quote.AccountCode.Length > 0 && accounts.ContainsKey(quote.AccountCode)) saleAccount = quote.AccountCode;
            Totals();
            SelectPage(posPage);
            Toast("Teklif satış sepetine aktarıldı.");
        }
    }

    private static (long, long, long, long) QuoteTotals(Quote quote)
    {
        var sums = Summarize(quote.Lines);
        return (sums.Subtotal, sums.Discount, sums.Vat, sums.Total);
    }

    private static TextBlock Section(string title, double top = 0)
    {
        var label = Label(title.ToUpper(new System.Globalization.CultureInfo("tr-TR")), 11, Muted, true);
        label.Margin = new Thickness(0, top, 0, 12);
        return label;
    }
}
