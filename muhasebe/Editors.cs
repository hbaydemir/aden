using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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

    private void OnOpen(DataGrid grid, Action open) =>
        grid.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(grid, source) is DataGridRow)
                Run(open);
        };

    // ───────────── Ürünler ─────────────

    private sealed record ProductRow(string Code, string Name, string Barcode, long Price, long Shop, int Minimum);

    private void AddProducts()
    {
        var body = AddPage("Ürünler", "Ürün kartları, barkodlar ve satış fiyatları", "", out var actions);
        productsPage = pages[^1].Content;
        var tools = SearchField("Kod, ürün adı veya barkod ara", out var search);
        tools.Width = 360;
        tools.HorizontalAlignment = HorizontalAlignment.Left;
        var grid = Table();
        Columns(grid, ("Kod", "Code", 1), ("Ürün", "Name", 2.2), ("Barkod", "Barcode", 1.4),
            ("Satış fiyatı", "Price", 1), ("Dükkân stoku", "Shop", 0.9), ("Raf alt sınırı", "Minimum", 0.9));
        object[] Filter() => products.Values.Where(p => Matches(search.Text.Trim(), p.Code, p.Name, p.Barcode))
            .OrderBy(p => p.Name)
            .Select(p => new ProductRow(p.Code, p.Name, p.Barcode, p.Price, stock.GetValueOrDefault(p.Code).Shop, p.Minimum)).ToArray();
        views.Add((grid, Filter));
        search.TextChanged += (_, _) => grid.ItemsSource = Filter();
        body.Children.Add(TableCard(grid, tools));

        Product Selected() => grid.SelectedItem is ProductRow row && products.TryGetValue(row.Code, out var product)
            ? product : throw new InvalidOperationException("Listeden bir ürün seç.");

        Append(actions, Act("Yeni ürün", () => Edit(null), "SuccessButton", ""));
        Append(actions, Act("Düzenle", () => Edit(Selected()), "PrimaryButton", ""));
        Append(actions, Act("Sil", () =>
        {
            var product = Selected();
            ConfirmDelete(product.Name, () =>
            {
                Save("product", product with { Archived = true });
                Toast("Ürün silindi.");
            });
        }, "DangerButton", ""));
        OnOpen(grid, () => Edit(Selected()));

        void Edit(Product? product)
        {
            var editing = product is not null;
            var code = Text(product?.Code ?? "");
            code.IsReadOnly = editing;
            var name = Text(product?.Name ?? "");
            var barcode = Text(product?.Barcode ?? "");
            var price = Text(((product?.Price ?? 0) / 100m).ToString("F2", culture));
            var minimum = Text((product?.Minimum ?? 0).ToString(culture));
            var form = Form(2,
                F(Field("Ürün kodu", code)), F(Field("Barkod", barcode)),
                F(Field("Ürün adı", name), 2),
                F(Field("Satış fiyatı (TL)", price)), F(Field("Raf alt sınırı", minimum)));
            var modal = ShowModal(editing ? "Ürünü düzenle" : "Yeni ürün", form, 560);
            Append(modal.Footer, Act("Vazgeç", modal.Close));
            Append(modal.Footer, Act(editing ? "Güncelle" : "Ürünü ekle", () =>
            {
                var id = Required(code).ToUpperInvariant();
                if (products.ContainsKey(id) != editing)
                    throw new InvalidOperationException(editing ? "Ürün başka bir cihazda silinmiş." : "Bu ürün kodu zaten kullanılıyor.");
                if (!editing && Read<Product>("product").Any(p => p.Value.Code.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Bu ürün kodu geçmiş kayıtlarda kullanılıyor.");
                var bar = barcode.Text.Trim();
                if (bar.Length > 0 && products.Values.Any(p => p.Code != id && p.Barcode == bar))
                    throw new InvalidOperationException("Barkod başka bir üründe kullanılıyor.");
                Save("product", new Product(id, Required(name), bar, Money(price, true), Quantity(minimum, true)));
                modal.Close();
                Toast(editing ? "Ürün güncellendi." : "Ürün eklendi.");
            }, editing ? "PrimaryButton" : "SuccessButton", editing ? "" : ""));
        }
    }

    // ───────────── Cariler ─────────────

    private sealed record AccountRow(string Code, string Name, string Contact, string Balance);

    private void AddAccounts()
    {
        var body = AddPage("Cariler", "Müşteri kartları, bakiyeler, tahsilat ve ödemeler", "", out var actions);
        var tools = SearchField("Cari kodu, ad veya iletişim ara", out var search);
        tools.Width = 360;
        tools.HorizontalAlignment = HorizontalAlignment.Left;
        var grid = Table();
        Columns(grid, ("Kod", "Code", 0.8), ("Cari", "Name", 1.6), ("İletişim", "Contact", 2), ("Bakiye", "Balance", 1));
        object[] Filter() => accounts.Values.Where(a => Matches(search.Text.Trim(), a.Code, a.Name, a.Contact))
            .OrderBy(a => a.Name).Select(a => new AccountRow(a.Code, a.Name, a.Contact.ReplaceLineEndings(" · "),
                Format(AccountBalance(a.Code)))).ToArray();
        views.Add((grid, Filter));
        search.TextChanged += (_, _) => grid.ItemsSource = Filter();
        var movements = View(() =>
            Read<Sale>("sale").Where(s => s.Value.Payments.Any(p => p.Method == "Vadeli"))
                .Select(s => new { Tarih = s.Entry.CreatedAt.LocalDateTime, Cari = s.Value.Account,
                    İşlem = "Vadeli satış", Açıklama = "", Tutar = Format(s.Value.Payments.Where(p => p.Method == "Vadeli").Sum(p => p.Amount)) })
                .Concat(Read<Cash>("cash").Where(c => c.Value.Account.Length > 0).Select(c =>
                    new { Tarih = c.Entry.CreatedAt.LocalDateTime, Cari = c.Value.Account,
                        İşlem = c.Value.Category, Açıklama = c.Value.Detail, Tutar = Format(c.Value.Amount) }))
                .OrderByDescending(item => item.Tarih).ToArray());
        body.Children.Add(Segments(out _, ("Cari listesi", TableCard(grid, tools)), ("Cari hareketleri", Card(movements))));

        Account Selected() => grid.SelectedItem is AccountRow row && accounts.TryGetValue(row.Code, out var item)
            ? item : throw new InvalidOperationException("Listeden bir cari seç.");
        Account? SelectedOrNull() => grid.SelectedItem is AccountRow row && accounts.TryGetValue(row.Code, out var item) ? item : null;

        Append(actions, Act("Tahsilat al", () => Movement("Tahsilat", 1, SelectedOrNull()), glyph: ""));
        Append(actions, Act("Ödeme yap", () => Movement("Cari ödeme", -1, SelectedOrNull()), glyph: ""));
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
            var code = Text(account?.Code ?? "");
            code.IsReadOnly = editing;
            var name = Text(account?.Name ?? "");
            var contact = Multiline(account?.Contact ?? "");
            var form = Form(2, F(Field("Cari kodu", code)), F(new Border()), F(Field("Ad / unvan", name), 2),
                F(Field("Adres ve iletişim", contact), 2));
            var modal = ShowModal(editing ? "Cariyi düzenle" : "Yeni cari", form, 560);
            Append(modal.Footer, Act("Vazgeç", modal.Close));
            Append(modal.Footer, Act(editing ? "Güncelle" : "Cariyi ekle", () =>
            {
                var id = Required(code).ToUpperInvariant();
                if (!editing && Read<Account>("account").Any(a => a.Value.Code.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Bu cari kodu daha önce kullanılmış.");
                if (editing && !accounts.ContainsKey(id))
                    throw new InvalidOperationException("Cari başka bir cihazda silinmiş.");
                Save("account", new Account(id, Required(name), contact.Text.Trim()));
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
            var balance = Label(account is null ? "" : $"Güncel bakiye: {Format(AccountBalance(account.Code))}", 12, Muted);
            picker.SelectionChanged += (_, _) => balance.Text = picker.SelectedItem is AccountChoice c
                ? $"Güncel bakiye: {Format(AccountBalance(c.Code))}" : "";
            var cari = new StackPanel();
            cari.Children.Add(Field("Cari", picker));
            balance.Margin = new Thickness(0, 6, 0, 0);
            cari.Children.Add(balance);
            var form = Form(2, F(cari, 2), F(Field("Tutar (TL)", amount)), F(Field("Açıklama", detail)));
            var modal = ShowModal(sign > 0 ? "Tahsilat al" : "Cariye ödeme yap", form, 520,
                subtitle: sign > 0 ? "Cariden alınan ödeme bakiyeyi azaltır." : "Cariye yapılan ödeme bakiyeyi artırır.");
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

    private long AccountBalance(string code) =>
        Read<Sale>("sale").Where(s => s.Value.Account == code)
            .Sum(s => s.Value.Payments.Where(p => p.Method == "Vadeli").Sum(p => p.Amount))
        - Read<Cash>("cash").Where(c => c.Value.Account == code).Sum(c => c.Value.Amount);

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
    private sealed record QuoteLineRow(Line Line, string Name, int Quantity, long UnitPrice, long Total);

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
            var picker = AccountBox(accountCode.Length > 0 ? accountCode : null);
            var customer = Text(quote?.Customer ?? "");
            var contact = Multiline(quote?.Contact ?? "", 72);
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is not AccountChoice choice || !accounts.TryGetValue(choice.Code, out var account)) return;
                accountCode = account.Code;
                customer.Text = account.Name;
                contact.Text = account.Contact;
            };
            customer.TextChanged += (_, _) => dirty = true;
            var manual = Act("Manuel müşteri", () =>
            {
                accountCode = "";
                picker.SelectedItem = null;
                picker.Text = "";
                customer.Clear();
                contact.Clear();
                customer.Focus();
            }, glyph: "");
            manual.VerticalAlignment = VerticalAlignment.Bottom;
            var pickRow = new DockPanel();
            DockPanel.SetDock(manual, Dock.Right);
            manual.Margin = new Thickness(SmallGap, 0, 0, 0);
            pickRow.Children.Add(manual);
            pickRow.Children.Add(Field("Kayıtlı cariden seç", picker));
            root.Children.Add(Section("Müşteri"));
            root.Children.Add(Form(2, F(pickRow, 2), F(Field("Müşteri / unvan", customer)), F(Field("Adres ve iletişim", contact))));

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

            var lines = Table();
            lines.AutoGenerateColumns = false;
            lines.Height = 220;
            Columns(lines, ("Ürün", "Name", 2.4), ("Adet", "Quantity", 0.7), ("Birim fiyat", "UnitPrice", 1), ("Tutar", "Total", 1));
            var linesCard = Card(lines);
            linesCard.Margin = new Thickness(0, Gap, 0, 0);
            root.Children.Add(linesCard);
            var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var totalLabel = Label("", 18, bold: true);
            totalLabel.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(totalLabel, Dock.Right);
            bottom.Children.Add(totalLabel);
            var remove = Act("Seçili kalemi çıkar", () =>
            {
                if (lines.SelectedItem is not QuoteLineRow row) throw new InvalidOperationException("Listeden bir kalem seç.");
                quoteLines.Remove(row.Line);
            }, "DangerButton", "");
            remove.HorizontalAlignment = HorizontalAlignment.Left;
            bottom.Children.Add(remove);
            root.Children.Add(bottom);

            void RenderLines()
            {
                lines.ItemsSource = quoteLines.Select(l => new QuoteLineRow(l, l.Name, l.Quantity, l.UnitPrice, l.UnitPrice * l.Quantity)).ToArray();
                totalLabel.Text = "Genel toplam  " + Format(Sum(quoteLines));
            }
            System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) => { dirty = true; RenderLines(); };
            quoteLines.CollectionChanged += changed;
            RenderLines();

            void AddLine()
            {
                var product = ResolveProduct(productBox);
                quoteLines.Add(new(product.Code, product.Name, Quantity(count), product.Price));
                productBox.SelectedItem = null;
                productBox.Text = "";
                count.Text = "1";
                productBox.Focus();
            }

            var modal = ShowModal(id is null ? "Yeni teklif" : "Teklifi düzenle", root, 820, 760);
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
                var saved = new Quote(Required(customer), contact.Text.Trim(), quoteLines.ToArray(), accountCode);
                if (id is Guid existing)
                {
                    if (!Quotes().ContainsKey(existing)) throw new InvalidOperationException("Teklif başka bir cihazda silinmiş.");
                    Save("quote-revision", new QuoteRevision(existing, saved));
                }
                else Save("quote", saved);
                modal.Close();
                Toast("Teklif kaydedildi.");
            }, "SuccessButton", ""));
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
                store.Setting("companyContact"), store.Setting("quoteLogo"));
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
                document.Blocks.Add(new Paragraph(new Run($"{line.Name} × {line.Quantity}   {Format(line.UnitPrice * line.Quantity)}")));
            document.Blocks.Add(new Paragraph(new Bold(new Run($"Toplam {Format(Sum(quote.Lines))}"))));
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

    private static TextBlock Section(string title, double top = 0)
    {
        var label = Label(title.ToUpper(new System.Globalization.CultureInfo("tr-TR")), 11, Muted, true);
        label.Margin = new Thickness(0, top, 0, 12);
        return label;
    }
}
